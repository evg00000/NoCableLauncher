// NoCableLauncher 2 — based on NoCableLauncher by Maxx53 (GPL v2).
// Altered source: device handling, memory patching and settings rewritten; RS_ASIO mode added.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NoCableLauncher
{
    class GameExitedException : Exception
    {
        public GameExitedException() : base(Tr.T("Игра закрылась", "The game has exited")) { }
    }

    static class Program
    {
        public const string Version = "2.1";
        public const string SteamUrl = "steam://rungameid/221680";
        const string ExeName = "Rocksmith2014";

        public static readonly string AppDir = Path.GetDirectoryName(Application.ExecutablePath);

        static volatile bool hotkeyPressed;

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Log.Init();
            try { Tr.Init(Config.Load().Language); }
            catch { Tr.Init("auto"); }

            bool created;
            using (Mutex mutex = new Mutex(true, "NoCableLauncher2_SingleInstance", out created))
            {
                if (!created)
                {
                    Error(Tr.T("Лончер уже запущен.", "The launcher is already running."));
                    return 1;
                }

                string arg = args.Length > 0 ? args[0].ToLowerInvariant() : "";
                try
                {
                    if (arg == "-restore" || arg == "/restore")
                    {
                        AudioSetup.Restore();
                        MessageBox.Show(Tr.T("Звуковые устройства и форматы возвращены.", "Audio devices and formats have been restored."), "NoCableLauncher");
                        return 0;
                    }

                    Config cfg = Config.Load();
                    bool wantSettings = arg == "-set" || arg == "/set" || !Config.Exists || (Control.ModifierKeys & Keys.Shift) != 0;

                    if (wantSettings)
                    {
                        // repair leftovers before showing devices
                        if (AudioSetup.HasPendingRestore) AudioSetup.Restore();
                        using (SettingsForm f = new SettingsForm(cfg))
                        {
                            Application.Run(f);
                            if (!f.LaunchRequested) return 0;
                        }
                    }

                    return Run(cfg);
                }
                catch (Exception ex)
                {
                    Log.Write(Tr.T("Ошибка: ", "Error: ") + ex);
                    try { AudioSetup.Restore(); } catch { }
                    Error(ex.Message);
                    return 1;
                }
            }
        }

        static void Error(string msg)
        {
            MessageBox.Show(msg + Tr.T("\n\nПодробности: NoCableLauncher.log", "\n\nDetails: NoCableLauncher.log"), Tr.T("NoCableLauncher — ошибка", "NoCableLauncher — error"),
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        static int Run(Config cfg)
        {
            if (AudioSetup.HasPendingRestore)
            {
                Log.Write(Tr.T("Найдены несохранённые изменения от прошлого запуска — возвращаю.", "Found unrestored changes from the previous run — restoring."));
                AudioSetup.Restore();
            }

            if (Process.GetProcessesByName(ExeName).Length > 0)
            {
                Error(Tr.T("Rocksmith уже запущен. Закройте игру и запустите её через лончер — ", "Rocksmith is already running. Close the game and start it through the launcher — ") +
                      Tr.T("игра выбирает устройство только при старте.", "the game picks its device only at startup."));
                return 1;
            }

            string gameDir = RsAsio.DetectGameDir(cfg);
            Log.Write(Tr.T("Папка игры: ", "Game folder: ") + (gameDir ?? Tr.T("не найдена", "not found")) + Tr.T(", режим: ", ", mode: ") + cfg.Mode);

            if (cfg.Mode == AudioMode.Asio)
                return RunAsio(cfg, gameDir);
            return RunWasapi(cfg, gameDir);
        }

        // ---------------- RS_ASIO mode ----------------

        static int RunAsio(Config cfg, string gameDir)
        {
            if (gameDir == null)
                throw new Exception(Tr.T("Не найдена папка игры. Укажите её в настройках (вкладка «Игра»).", "Game folder not found. Set it in the settings (\"Game\" tab)."));
            RsAsio.Install(gameDir);
            RsAsio.Configure(cfg, gameDir);
            StartGame(cfg);
            Log.Write(Tr.T("Игра запущена в режиме RS_ASIO.", "Game started in RS_ASIO mode."));
            return 0;
        }

        // ---------------- NoCable (WASAPI patch) mode ----------------

        static int RunWasapi(Config cfg, string gameDir)
        {
            // RS_ASIO and the memory patch don't work together
            if (gameDir != null && RsAsio.IsInstalled(gameDir))
            {
                try { RsAsio.Disable(gameDir); }
                catch (Exception ex) { Log.Write(Tr.T("Не удалось убрать RS_ASIO: ", "Could not remove RS_ASIO: ") + ex.Message); }
            }

            byte[] vid = Patcher.ParseId(cfg.Vid), pid = Patcher.ParseId(cfg.Pid);
            byte[] vid2 = null, pid2 = null;
            if (cfg.Multiplayer)
            {
                vid2 = Patcher.ParseId(cfg.Vid2);
                pid2 = Patcher.ParseId(cfg.Pid2);
            }
            Log.Write(Tr.T("Игрок 1: VID ", "Player 1: VID ") + cfg.Vid + " PID " + cfg.Pid + (cfg.Multiplayer ? Tr.T(", игрок 2: VID ", ", player 2: VID ") + cfg.Vid2 + " PID " + cfg.Pid2 : ""));

            int hotkeyId = 0;
            try
            {
                AudioSetup.Prepare(cfg, cfg.Multiplayer);

                if (cfg.Multiplayer)
                {
                    hotkeyId = HotKeyManager.RegisterHotKey(Keys.M, KeyModifiers.Control);
                    HotKeyManager.HotKeyPressed += delegate { hotkeyPressed = true; };
                }

                StartGame(cfg);
                Patcher patcher = AttachAndPatch(cfg, vid, pid);

                using (patcher)
                {
                    Process game = patcher.Game;
                    Stopwatch since = Stopwatch.StartNew();
                    bool restoredEarly = false;

                    while (!game.WaitForExit(200))
                    {
                        if (cfg.Multiplayer && hotkeyPressed)
                        {
                            hotkeyPressed = false;
                            if (patcher.Patch(vid2, pid2))
                                Log.Write(Tr.T("Ctrl+M: пропатчено для игрока 2", "Ctrl+M: patched for player 2"));
                            else
                                Log.Write(Tr.T("Ctrl+M: не удалось записать ID игрока 2", "Ctrl+M: could not write player 2 IDs"));
                            AudioSetup.RevealPlayer2(cfg);
                        }

                        if (!restoredEarly && cfg.RestoreDelaySec > 0 && since.Elapsed.TotalSeconds >= cfg.RestoreDelaySec)
                        {
                            restoredEarly = true;
                            Log.Write(Tr.T("Прошло ", "") + cfg.RestoreDelaySec + Tr.T(" с — возвращаю устройства, не дожидаясь выхода из игры", " s passed — restoring devices without waiting for the game to exit"));
                            AudioSetup.Restore();
                        }
                    }
                    Log.Write(Tr.T("Игра закрыта (код ", "Game closed (exit code ") + SafeExitCode(game) + ")");
                }
            }
            finally
            {
                if (hotkeyId != 0)
                {
                    try { HotKeyManager.UnregisterHotKey(hotkeyId); } catch { }
                }
                AudioSetup.Restore();
            }
            return 0;
        }

        static string SafeExitCode(Process p)
        {
            try { return p.ExitCode.ToString(); } catch { return "?"; }
        }

        static void StartGame(Config cfg)
        {
            ProcessStartInfo si;
            if (cfg.IsSteam)
            {
                si = new ProcessStartInfo(SteamUrl);
            }
            else
            {
                if (!File.Exists(cfg.GameExe))
                    throw new Exception(Tr.T("Не найден exe игры: ", "Game exe not found: ") + cfg.GameExe + Tr.T("\nПроверьте путь в настройках.", "\nCheck the path in the settings."));
                si = new ProcessStartInfo(cfg.GameExe);
                si.WorkingDirectory = Path.GetDirectoryName(cfg.GameExe);
            }
            si.UseShellExecute = true;
            Log.Write(Tr.T("Запуск: ", "Starting: ") + si.FileName);
            Process.Start(si);
        }

        static Process WaitForProcess(int timeoutSec, int excludePid)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < timeoutSec)
            {
                foreach (Process p in Process.GetProcessesByName(ExeName))
                {
                    if (p.Id == excludePid) continue;
                    try { if (!p.HasExited) return p; } catch { }
                }
                Thread.Sleep(50);
            }
            return null;
        }

        /// <summary>
        /// Find the game, locate the cable IDs and patch them. Handles the case where the first process
        /// exits immediately (Steam relaunching the exe) by attaching to the next one.
        /// </summary>
        static Patcher AttachAndPatch(Config cfg, byte[] vid, byte[] pid)
        {
            IntPtr cached = IntPtr.Zero;
            long c;
            if (long.TryParse(cfg.OffsetVid, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out c) && c > 0)
                cached = Patcher.P(c);

            int lastPid = -1;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                Process game = WaitForProcess(attempt == 1 ? 180 : 30, lastPid);
                if (game == null)
                {
                    if (attempt == 1)
                        throw new Exception(Tr.T("Процесс ", "Process ") + ExeName + Tr.T(".exe не появился за 3 минуты.", ".exe did not appear within 3 minutes."));
                    break;
                }
                lastPid = game.Id;
                Log.Write(Tr.T("Процесс игры найден, PID ", "Game process found, PID ") + game.Id + Tr.T(" (попытка ", " (attempt ") + attempt + ")");

                Patcher patcher = new Patcher(game);
                try
                {
                    if (!patcher.Locate(vid, pid, cached, cfg.ScanTimeoutSec))
                    {
                        patcher.Dispose();
                        throw new Exception(Tr.T("Не удалось найти ID кабеля в памяти игры за ", "Could not find the cable IDs in game memory within ") + cfg.ScanTimeoutSec + Tr.T(" с.\n", " s.\n") +
                                            Tr.T("Попробуйте увеличить время ожидания на вкладке «Дополнительно». ", "Try increasing the wait time on the \"Advanced\" tab. ") +
                                            Tr.T("Если игра обновилась и формат данных изменился — пришлите NoCableLauncher.log.", "If the game was updated and the data layout changed, please send NoCableLauncher.log."));
                    }
                    if (!patcher.Patch(vid, pid))
                    {
                        patcher.Dispose();
                        throw new Exception(Tr.T("Не удалось записать ID устройства в память игры.", "Could not write the device IDs into game memory."));
                    }
                    Log.Write(Tr.T("Пропатчено: VID @0x", "Patched: VID @0x") + Patcher.U(patcher.AddrVid).ToString("X8") +
                              ", PID @0x" + Patcher.U(patcher.AddrPid).ToString("X8"));

                    string newVid = Patcher.U(patcher.AddrVid).ToString("X8");
                    string newPid = Patcher.U(patcher.AddrPid).ToString("X8");
                    if (newVid != cfg.OffsetVid || newPid != cfg.OffsetPid)
                    {
                        cfg.OffsetVid = newVid;
                        cfg.OffsetPid = newPid;
                        try { cfg.Save(); } catch (Exception ex) { Log.Write(Tr.T("Не удалось сохранить адрес: ", "Could not save the address: ") + ex.Message); }
                    }

                    patcher.Guard(vid, pid, 15);
                    if (game.HasExited) throw new GameExitedException();
                    return patcher;
                }
                catch (GameExitedException)
                {
                    patcher.Dispose();
                    Log.Write(Tr.T("Процесс ", "Process ") + game.Id + Tr.T(" завершился — жду перезапуска игры Steam'ом", " exited — waiting for Steam to relaunch the game"));
                }
                catch
                {
                    patcher.Dispose();
                    throw;
                }
            }
            throw new Exception(Tr.T("Игра закрылась сразу после запуска.", "The game exited right after start."));
        }
    }
}
