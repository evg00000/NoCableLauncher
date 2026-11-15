using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace NoCableLauncher
{
    /// <summary>
    /// RS_ASIO (https://github.com/mdias/rs_asio, MIT) integration.
    /// RS_ASIO is a DLL that the game loads itself (avrt.dll proxy) and that exposes ASIO devices
    /// as fake WASAPI devices — it cannot be merged into this launcher, but the launcher can
    /// install/remove it, configure it and switch between the two modes.
    /// </summary>
    public static class RsAsio
    {
        public static readonly string[] Files = { "avrt.dll", "RS_ASIO.dll" };
        const string DisabledDir = "RS_ASIO_disabled";

        /// <summary>Folder next to the launcher where the user puts files from the RS_ASIO release zip.</summary>
        public static string SourceDir { get { return Path.Combine(Program.AppDir, "RS_ASIO"); } }

        public static bool SourceAvailable
        {
            get
            {
                foreach (string f in Files)
                    if (!File.Exists(Path.Combine(SourceDir, f))) return false;
                return true;
            }
        }

        public static bool IsInstalled(string gameDir)
        {
            return !string.IsNullOrEmpty(gameDir) && File.Exists(Path.Combine(gameDir, "RS_ASIO.dll"))
                && File.Exists(Path.Combine(gameDir, "avrt.dll"));
        }

        // ---------- ASIO drivers ----------

        /// <summary>ASIO drivers registered for 32-bit apps (the game is 32-bit; our process is x86, so
        /// registry access is redirected to WOW6432Node automatically).</summary>
        public static List<string> GetDrivers()
        {
            List<string> res = new List<string>();
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ASIO"))
                {
                    if (k != null)
                        // RS_ASIO matches drivers by the registry key name
                        foreach (string n in k.GetSubKeyNames())
                            res.Add(n);
                }
            }
            catch (Exception ex) { Log.Write("Не удалось прочитать список ASIO: " + ex.Message); }
            res.Sort(StringComparer.OrdinalIgnoreCase);
            return res;
        }

        // ---------- Game folder ----------

        public static string DetectGameDir(Config cfg)
        {
            if (!string.IsNullOrEmpty(cfg.GameDir) && File.Exists(Path.Combine(cfg.GameDir, "Rocksmith2014.exe")))
                return cfg.GameDir;
            if (!cfg.IsSteam && !string.IsNullOrEmpty(cfg.GameExe) && File.Exists(cfg.GameExe))
                return Path.GetDirectoryName(cfg.GameExe);
            return FindSteamGameDir();
        }

        public static string FindSteamGameDir()
        {
            List<string> libs = new List<string>();
            string steam = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    if (k != null) steam = k.GetValue("SteamPath") as string;
                if (steam == null)
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam"))
                        if (k != null) steam = k.GetValue("InstallPath") as string;
            }
            catch { }
            if (string.IsNullOrEmpty(steam)) return null;
            steam = steam.Replace('/', '\\');
            libs.Add(steam);

            foreach (string vdf in new string[] {
                Path.Combine(steam, @"steamapps\libraryfolders.vdf"),
                Path.Combine(steam, @"config\libraryfolders.vdf") })
            {
                try
                {
                    if (!File.Exists(vdf)) continue;
                    string text = File.ReadAllText(vdf);
                    // new format: "path"  "D:\\SteamLibrary"; old format: "1"  "D:\\SteamLibrary"
                    foreach (Match m in Regex.Matches(text, "\"(?:path|\\d+)\"\\s+\"([^\"]+)\""))
                    {
                        string p = m.Groups[1].Value.Replace(@"\\", @"\");
                        if (Directory.Exists(p) && !libs.Contains(p)) libs.Add(p);
                    }
                }
                catch { }
            }

            foreach (string lib in libs)
            {
                string dir = Path.Combine(lib, @"steamapps\common\Rocksmith2014");
                if (File.Exists(Path.Combine(dir, "Rocksmith2014.exe"))) return dir;
            }
            return null;
        }

        // ---------- Install / remove ----------

        static bool SameFile(string a, string b)
        {
            if (!File.Exists(a) || !File.Exists(b)) return false;
            byte[] x = File.ReadAllBytes(a), y = File.ReadAllBytes(b);
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
            return true;
        }

        public static void Install(string gameDir)
        {
            if (!SourceAvailable)
                throw new Exception("Не найдены файлы RS_ASIO.\nСкачайте release-*.zip со страницы github.com/mdias/rs_asio/releases " +
                                    "и распакуйте avrt.dll, RS_ASIO.dll и RS_ASIO.ini в папку:\n" + SourceDir);
            foreach (string f in Files)
            {
                string src = Path.Combine(SourceDir, f), dst = Path.Combine(gameDir, f);
                if (!SameFile(src, dst))
                {
                    File.Copy(src, dst, true);
                    Log.Write("RS_ASIO: скопирован " + f);
                }
            }
            string ini = Path.Combine(gameDir, "RS_ASIO.ini");
            string iniSrc = Path.Combine(SourceDir, "RS_ASIO.ini");
            if (!File.Exists(ini) && File.Exists(iniSrc)) File.Copy(iniSrc, ini);
        }

        /// <summary>Move RS_ASIO dlls out of the game folder (needed for WASAPI/NoCable mode).</summary>
        public static void Disable(string gameDir)
        {
            if (!IsInstalled(gameDir)) return;
            string bak = Path.Combine(gameDir, DisabledDir);
            Directory.CreateDirectory(bak);
            foreach (string f in Files)
            {
                string p = Path.Combine(gameDir, f);
                if (!File.Exists(p)) continue;
                string dst = Path.Combine(bak, f);
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(p, dst);
            }
            Log.Write("RS_ASIO убран из папки игры в " + bak);
        }

        // ---------- Configuration ----------

        public static void Configure(Config cfg, string gameDir)
        {
            IniFile f = new IniFile(Path.Combine(gameDir, "RS_ASIO.ini"));
            string drv = cfg.AsioDriver ?? "";

            f.Set("Config", "EnableWasapiOutputs", "0");
            f.Set("Config", "EnableWasapiInputs", "0");
            f.Set("Config", "EnableAsio", "1");

            f.Set("Asio.Output", "Driver", drv);
            f.Set("Asio.Output", "BaseChannel", cfg.AsioOutputChannel.ToString());

            f.Set("Asio.Input.0", "Driver", drv);
            f.Set("Asio.Input.0", "Channel", cfg.AsioInputChannel.ToString());

            f.Set("Asio.Input.1", "Driver", cfg.Multiplayer ? drv : "");
            f.Set("Asio.Input.1", "Channel", cfg.AsioInput2Channel.ToString());

            f.Set("Asio.Input.Mic", "Driver", "");
            f.Save();
            Log.Write("RS_ASIO.ini: драйвер \"" + drv + "\", вход " + cfg.AsioInputChannel + ", выход " + cfg.AsioOutputChannel);

            if (cfg.AsioSetRocksmithIni) SetRocksmithIni(gameDir);
        }

        static void SetRocksmithIni(string gameDir)
        {
            string path = Path.Combine(gameDir, "Rocksmith.ini");
            if (!File.Exists(path)) { Log.Write("Rocksmith.ini не найден — пропускаю"); return; }
            string bak = path + ".ncl-backup";
            if (!File.Exists(bak)) File.Copy(path, bak);

            IniFile f = new IniFile(path);
            f.Set("Audio", "ExclusiveMode", "1");
            f.Set("Audio", "Win32UltraLowLatencyMode", "1");
            f.Save();
            Log.Write("Rocksmith.ini: ExclusiveMode=1, Win32UltraLowLatencyMode=1");
        }
    }
}
