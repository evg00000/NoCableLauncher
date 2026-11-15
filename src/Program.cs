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
        public GameExitedException() : base("Игра закрылась") { }
    }

    static class Program
    {
        public const string Version = "2.0";
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

            bool created;
            using (Mutex mutex = new Mutex(true, "NoCableLauncher2_SingleInstance", out created))
            {
                if (!created)
                {
                    Error("Лончер уже запущен.");
                    return 1;
                }

                string arg = args.Length > 0 ? args[0].ToLowerInvariant() : "";
                try
                {
                    if (arg == "-restore" || arg == "/restore")
                    {
                        AudioSetup.Restore();
                        MessageBox.Show("Звуковые устройства и форматы возвращены.", "NoCableLauncher");
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
                    Log.Write("Ошибка: " + ex);
                    try { AudioSetup.Restore(); } catch { }
                    Error(ex.Message);
                    return 1;
                }
            }
        }

        static void Error(string msg)
        {
            MessageBox.Show(msg + "\n\nПодробности: NoCableLauncher.log", "NoCableLauncher — ошибка",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        static int Run(Config cfg)
        {
            if (AudioSetup.HasPendingRestore)
            {
                Log.Write("Найдены несохранённые изменения от прошлого запуска — возвращаю.");
                AudioSetup.Restore();
            }

            if (Process.GetProcessesByName(ExeName).Length > 0)
            {
                Error("Rocksmith уже запущен. Закройте игру и запустите её через лончер — " +
                      "игра выбирает устройство только при старте.");
                return 1;
            }

            string gameDir = RsAsio.DetectGameDir(cfg);
            Log.Write("Папка игры: " + (gameDir ?? "не найдена") + ", режим: " + cfg.Mode);

            if (cfg.Mode == AudioMode.Asio)
                return RunAsio(cfg, gameDir);
            return RunWasapi(cfg, gameDir);
        }

        // ---------------- RS_ASIO mode ----------------

        static int RunAsio(Config cfg, string gameDir)
        {
            if (gameDir == null)
                throw new Exception("Не найдена папка игры. Укажите её в настройках (вкладка «Игра»).");
            RsAsio.Install(gameDir);
            RsAsio.Configure(cfg, gameDir);
            StartGame(cfg);
            Log.Write("Игра запущена в режиме RS_ASIO.");
            return 0;
        }

        // ---------------- NoCable (WASAPI patch) mode ----------------

        static int RunWasapi(Config cfg, string gameDir)
        {
            // RS_ASIO and the memory patch don't work together
            if (gameDir != null && RsAsio.IsInstalled(gameDir))
            {
                try { RsAsio.Disable(gameDir); }
                catch (Exception ex) { Log.Write("Не удалось убрать RS_ASIO: " + ex.Message); }
            }

            byte[] vid = Patcher.ParseId(cfg.Vid), pid = Patcher.ParseId(cfg.Pid);
            byte[] vid2 = null, pid2 = null;
            if (cfg.Multiplayer)
            {
                vid2 = Patcher.ParseId(cfg.Vid2);
                pid2 = Patcher.ParseId(cfg.Pid2);
            }
            Log.Write("Игрок 1: VID " + cfg.Vid + " PID " + cfg.Pid + (cfg.Multiplayer ? ", игрок 2: VID " + cfg.Vid2 + " PID " + cfg.Pid2 : ""));

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
                                Log.Write("Ctrl+M: пропатчено для игрока 2");
                            else
                                Log.Write("Ctrl+M: не удалось записать ID игрока 2");
                            AudioSetup.RevealPlayer2(cfg);
                        }

                        if (!restoredEarly && cfg.RestoreDelaySec > 0 && since.Elapsed.TotalSeconds >= cfg.RestoreDelaySec)
                        {
                            restoredEarly = true;
                            Log.Write("Прошло " + cfg.RestoreDelaySec + " с — возвращаю устройства, не дожидаясь выхода из игры");
                            AudioSetup.Restore();
                        }
                    }
                    Log.Write("Игра закрыта (код " + SafeExitCode(game) + ")");
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
                    throw new Exception("Не найден exe игры: " + cfg.GameExe + "\nПроверьте путь в настройках.");
                si = new ProcessStartInfo(cfg.GameExe);
                si.WorkingDirectory = Path.GetDirectoryName(cfg.GameExe);
            }
            si.UseShellExecute = true;
            Log.Write("Запуск: " + si.FileName);
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
                        throw new Exception("Процесс " + ExeName + ".exe не появился за 3 минуты.");
                    break;
                }
                lastPid = game.Id;
                Log.Write("Процесс игры найден, PID " + game.Id + " (попытка " + attempt + ")");

                Patcher patcher = new Patcher(game);
                try
                {
                    if (!patcher.Locate(vid, pid, cached, cfg.ScanTimeoutSec))
                    {
                        patcher.Dispose();
                        throw new Exception("Не удалось найти ID кабеля в памяти игры за " + cfg.ScanTimeoutSec + " с.\n" +
                                            "Попробуйте увеличить время ожидания на вкладке «Дополнительно». " +
                                            "Если игра обновилась и формат данных изменился — пришлите NoCableLauncher.log.");
                    }
                    if (!patcher.Patch(vid, pid))
                    {
                        patcher.Dispose();
                        throw new Exception("Не удалось записать ID устройства в память игры.");
                    }
                    Log.Write("Пропатчено: VID @0x" + Patcher.U(patcher.AddrVid).ToString("X8") +
                              ", PID @0x" + Patcher.U(patcher.AddrPid).ToString("X8"));

                    string newVid = Patcher.U(patcher.AddrVid).ToString("X8");
                    string newPid = Patcher.U(patcher.AddrPid).ToString("X8");
                    if (newVid != cfg.OffsetVid || newPid != cfg.OffsetPid)
                    {
                        cfg.OffsetVid = newVid;
                        cfg.OffsetPid = newPid;
                        try { cfg.Save(); } catch (Exception ex) { Log.Write("Не удалось сохранить адрес: " + ex.Message); }
                    }

                    patcher.Guard(vid, pid, 15);
                    if (game.HasExited) throw new GameExitedException();
                    return patcher;
                }
                catch (GameExitedException)
                {
                    patcher.Dispose();
                    Log.Write("Процесс " + game.Id + " завершился — жду перезапуска игры Steam'ом");
                }
                catch
                {
                    patcher.Dispose();
                    throw;
                }
            }
            throw new Exception("Игра закрылась сразу после запуска.");
        }
    }
}
