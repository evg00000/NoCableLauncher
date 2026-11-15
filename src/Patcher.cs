using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace NoCableLauncher
{
    /// <summary>
    /// Replaces the Real Tone Cable VID/PID inside the running Rocksmith2014.exe with the IDs of the user's device.
    ///
    /// Why the old launcher said "Offcets not found":
    ///  * it waited a fixed 3 s after the process appeared and scanned exactly once — the Steam DRM stub
    ///    may not have unpacked the exe by then, so the pattern simply wasn't in memory yet;
    ///  * it read the whole module with one ReadProcessMemory call; a single unreadable page makes the call
    ///    fail and the buffer stays zero-filled (the result was never checked);
    ///  * the matcher didn't reset its counter between start positions and could run past the buffer end.
    /// Here we read region-by-region, retry until a timeout, and also accept memory that is already patched.
    /// </summary>
    public class Patcher : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(int access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, [Out] byte[] buf, IntPtr size, out IntPtr read);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr written);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualProtectEx(IntPtr h, IntPtr addr, IntPtr size, uint newProtect, out uint oldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualQueryEx(IntPtr h, IntPtr addr, out MEMORY_BASIC_INFORMATION info, IntPtr len);

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        const int PROCESS_VM_OPERATION = 0x0008, PROCESS_VM_READ = 0x0010, PROCESS_VM_WRITE = 0x0020,
                  PROCESS_QUERY_INFORMATION = 0x0400, SYNCHRONIZE = 0x00100000;
        const uint MEM_COMMIT = 0x1000, PAGE_GUARD = 0x100, PAGE_NOACCESS = 0x01, PAGE_READWRITE = 0x04;

        public static readonly byte[] RtVid = { 0xBA, 0x12 };   // 0x12BA little-endian
        public static readonly byte[] RtPid = { 0xFF, 0x00 };   // 0x00FF
        // Fragment of the game's table of accepted USB IDs: 12BA, 0A92, C010, C011, 00FF
        static readonly byte[] Middle = { 0x92, 0x0A, 0x10, 0xC0, 0x11, 0xC0 };
        const int PidOffsetInPattern = 8;

        /// <summary>Address as unsigned number (x86: addresses above 2 GB must not turn negative).</summary>
        public static long U(IntPtr p) { return IntPtr.Size == 4 ? (long)(uint)p.ToInt32() : p.ToInt64(); }
        public static IntPtr P(long a) { return IntPtr.Size == 4 ? new IntPtr(unchecked((int)(uint)a)) : new IntPtr(a); }

        public readonly Process Game;
        IntPtr handle;
        public IntPtr AddrVid { get; private set; }
        public IntPtr AddrPid { get; private set; }

        public Patcher(Process game)
        {
            Game = game;
            handle = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION | SYNCHRONIZE,
                                 false, game.Id);
            if (handle == IntPtr.Zero)
                throw new Exception(Tr.T("Не удалось открыть процесс игры (ошибка ", "Could not open the game process (error ") + Marshal.GetLastWin32Error() +
                                    Tr.T("). Если игра запущена от администратора — запустите лончер так же.", "). If the game runs as administrator, run the launcher the same way."));
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
        }

        public static byte[] ParseId(string hex)
        {
            int v = int.Parse(hex.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (v < 0 || v > 0xFFFF) throw new FormatException(Tr.T("VID/PID должен быть 4 hex-цифры: ", "VID/PID must be 4 hex digits: ") + hex);
            return new byte[] { (byte)(v & 0xFF), (byte)(v >> 8) };
        }

        static byte[] BuildPattern(byte[] vid, byte[] pid)
        {
            byte[] p = new byte[10];
            p[0] = vid[0]; p[1] = vid[1];
            Array.Copy(Middle, 0, p, 2, Middle.Length);
            p[8] = pid[0]; p[9] = pid[1];
            return p;
        }

        byte[] Read(IntPtr addr, int len)
        {
            byte[] b = new byte[len];
            IntPtr got;
            if (!ReadProcessMemory(handle, addr, b, (IntPtr)len, out got) || got.ToInt64() != len) return null;
            return b;
        }

        static bool Eq(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Check a cached address: true if it holds the cable IDs or the IDs we want.</summary>
        public bool TryAddress(IntPtr vidAddr, byte[] vid, byte[] pid)
        {
            if (vidAddr == IntPtr.Zero) return false;
            byte[] b = Read(vidAddr, 10);
            if (b == null) return false;
            if (Eq(b, BuildPattern(RtVid, RtPid)) || Eq(b, BuildPattern(vid, pid)))
            {
                AddrVid = vidAddr;
                AddrPid = P(U(vidAddr) + PidOffsetInPattern);
                return true;
            }
            return false;
        }

        static int IndexOf(byte[] buf, int len, byte[] pat)
        {
            int last = len - pat.Length;
            for (int i = 0; i <= last; i++)
            {
                int j = 0;
                while (j < pat.Length && buf[i + j] == pat[j]) j++;
                if (j == pat.Length) return i;
            }
            return -1;
        }

        /// <summary>Scan readable committed regions in [start, end) for either pattern.</summary>
        bool Scan(long start, long end, byte[][] patterns)
        {
            const int Chunk = 1 << 20;
            byte[] buf = new byte[Chunk + 16];
            long addr = start;
            int infoSize = Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION));

            while (addr < end)
            {
                MEMORY_BASIC_INFORMATION mbi;
                if (VirtualQueryEx(handle, P(addr), out mbi, (IntPtr)infoSize) == IntPtr.Zero) break;
                long rBase = U(mbi.BaseAddress);
                long rSize = U(mbi.RegionSize);
                if (rSize <= 0) break;
                long rEnd = Math.Min(rBase + rSize, end);

                bool readable = mbi.State == MEM_COMMIT && (mbi.Protect & PAGE_GUARD) == 0 && (mbi.Protect & PAGE_NOACCESS) == 0 && mbi.Protect != 0;
                if (readable)
                {
                    long pos = Math.Max(rBase, addr);
                    while (pos < rEnd)
                    {
                        int want = (int)Math.Min(Chunk + 16, rEnd - pos);
                        IntPtr got;
                        if (ReadProcessMemory(handle, P(pos), buf, (IntPtr)want, out got) && got.ToInt64() > 0)
                        {
                            int n = (int)got.ToInt64();
                            foreach (byte[] pat in patterns)
                            {
                                int idx = IndexOf(buf, n, pat);
                                if (idx >= 0)
                                {
                                    AddrVid = P(pos + idx);
                                    AddrPid = P(pos + idx + PidOffsetInPattern);
                                    return true;
                                }
                            }
                        }
                        pos += Chunk;   // 16-byte overlap so a pattern crossing chunks is not lost
                    }
                }
                addr = rBase + rSize;
            }
            return false;
        }

        bool GetModule(out long start, out long end)
        {
            start = end = 0;
            try
            {
                Game.Refresh();
                ProcessModule m = Game.MainModule;
                start = U(m.BaseAddress);
                end = start + m.ModuleMemorySize;
                return true;
            }
            catch { return false; }   // process is still initializing
        }

        /// <summary>
        /// Wait until the IDs table appears in the game memory and remember its address.
        /// </summary>
        public bool Locate(byte[] vid, byte[] pid, IntPtr cachedVidAddr, int timeoutSec)
        {
            byte[][] patterns = { BuildPattern(RtVid, RtPid), BuildPattern(vid, pid) };
            Stopwatch sw = Stopwatch.StartNew();
            int attempt = 0;
            bool fullScanDone = false;

            while (sw.Elapsed.TotalSeconds < timeoutSec)
            {
                if (Game.HasExited) throw new GameExitedException();
                attempt++;

                if (TryAddress(cachedVidAddr, vid, pid))
                {
                    Log.Write(Tr.T("Адрес из кэша подошёл: 0x", "Cached address is valid: 0x") + U(AddrVid).ToString("X8") + Tr.T(" (попытка ", " (attempt ") + attempt + ")");
                    return true;
                }

                long ms, me;
                if (GetModule(out ms, out me) && Scan(ms, me, patterns))
                {
                    Log.Write(Tr.T("Найдено в модуле: 0x", "Found in module: 0x") + U(AddrVid).ToString("X8") + Tr.T(" (попытка ", " (attempt ") + attempt + ", " + sw.ElapsedMilliseconds + Tr.T(" мс)", " ms)"));
                    return true;
                }

                // after a while, look through the whole address space once (heap copies, unusual builds)
                if (!fullScanDone && sw.Elapsed.TotalSeconds > timeoutSec / 2.0)
                {
                    fullScanDone = true;
                    Log.Write(Tr.T("В модуле не найдено, сканирую всю память процесса...", "Not found in module, scanning the whole process memory..."));
                    if (Scan(0x10000, 0xFFFF0000L, patterns))
                    {
                        Log.Write(Tr.T("Найдено вне модуля: 0x", "Found outside module: 0x") + U(AddrVid).ToString("X8"));
                        return true;
                    }
                }
                Thread.Sleep(attempt < 50 ? 100 : 500);
            }
            return false;
        }

        bool Write(IntPtr addr, byte[] data)
        {
            IntPtr written;
            if (WriteProcessMemory(handle, addr, data, (IntPtr)data.Length, out written) && written.ToInt64() == data.Length)
                return true;

            // read-only page (.rdata) — unprotect, write, restore
            uint old;
            if (!VirtualProtectEx(handle, addr, (IntPtr)data.Length, PAGE_READWRITE, out old)) return false;
            bool ok = WriteProcessMemory(handle, addr, data, (IntPtr)data.Length, out written) && written.ToInt64() == data.Length;
            uint dummy;
            VirtualProtectEx(handle, addr, (IntPtr)data.Length, old, out dummy);
            return ok;
        }

        public bool Patch(byte[] vid, byte[] pid)
        {
            if (!Write(AddrVid, vid) || !Write(AddrPid, pid)) return false;
            return IsPatched(vid, pid);
        }

        public bool IsPatched(byte[] vid, byte[] pid)
        {
            return Eq(Read(AddrVid, 2), vid) && Eq(Read(AddrPid, 2), pid);
        }

        /// <summary>For a few seconds after patching make sure the values were not overwritten (DRM unpacking).</summary>
        public void Guard(byte[] vid, byte[] pid, int seconds)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds && !Game.HasExited)
            {
                Thread.Sleep(250);
                if (!IsPatched(vid, pid))
                {
                    Log.Write(Tr.T("Значения в памяти изменились — патчу повторно", "Memory values changed — patching again"));
                    Patch(vid, pid);
                }
            }
        }
    }
}
