using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NoCableLauncher
{
    /// <summary>
    /// Minimal INI editor that keeps comments and order of lines (needed for RS_ASIO.ini and Rocksmith.ini).
    /// </summary>
    public class IniFile
    {
        readonly List<string> lines = new List<string>();
        public string Path;

        public IniFile(string path)
        {
            Path = path;
            if (File.Exists(path))
                lines.AddRange(File.ReadAllLines(path));
        }

        static bool IsSection(string line, out string name)
        {
            string t = line.Trim();
            name = null;
            if (t.StartsWith("[") && t.EndsWith("]"))
            {
                name = t.Substring(1, t.Length - 2).Trim();
                return true;
            }
            return false;
        }

        static bool IsKey(string line, string key, out string value)
        {
            value = null;
            string t = line.TrimStart();
            if (t.StartsWith(";") || t.StartsWith("#")) return false;
            int eq = t.IndexOf('=');
            if (eq <= 0) return false;
            if (!string.Equals(t.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase)) return false;
            value = t.Substring(eq + 1).Trim();
            return true;
        }

        public string Get(string section, string key, string def)
        {
            string cur = "";
            foreach (string l in lines)
            {
                string s;
                if (IsSection(l, out s)) { cur = s; continue; }
                string v;
                if (string.Equals(cur, section, StringComparison.OrdinalIgnoreCase) && IsKey(l, key, out v))
                    return v;
            }
            return def;
        }

        public void Set(string section, string key, string value)
        {
            string cur = "";
            int sectionEnd = -1;
            bool inSection = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string s;
                if (IsSection(lines[i], out s))
                {
                    if (inSection) break;
                    cur = s;
                    inSection = string.Equals(cur, section, StringComparison.OrdinalIgnoreCase);
                    if (inSection) sectionEnd = i + 1;
                    continue;
                }
                if (!inSection) continue;
                string v;
                if (IsKey(lines[i], key, out v))
                {
                    lines[i] = key + "=" + value;
                    return;
                }
                if (lines[i].Trim().Length > 0) sectionEnd = i + 1;
            }

            if (sectionEnd < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add(key + "=" + value);
            }
            else
            {
                lines.Insert(sectionEnd, key + "=" + value);
            }
        }

        public void Save()
        {
            File.WriteAllLines(Path, lines.ToArray(), new UTF8Encoding(false));
        }
    }

    public enum AudioMode { Wasapi, Asio }

    public class Config
    {
        // [Game]
        public bool IsSteam = true;
        public string GameExe = "";      // only when not Steam
        public string GameDir = "";      // auto-detected if empty

        // [Mode]
        public AudioMode Mode = AudioMode.Wasapi;

        // [Player1] / [Player2]
        public string InputId = "";
        public string Vid = "0000";
        public string Pid = "0000";
        public bool ManualIds = false;

        public bool Multiplayer = false;
        public string Input2Id = "";
        public string Vid2 = "0000";
        public string Pid2 = "0000";
        public bool ManualIds2 = false;

        // [Devices]
        public string OutputId = "";
        public bool HideOtherInputs = true;
        public bool HideOtherOutputs = true;
        public bool SetDefaultDevices = true;
        public int RestoreDelaySec = 0;   // 0 = restore when game exits

        // [Format]
        public bool SetFormat = true;
        public int SampleRate = 48000;
        public int BitDepth = 24;

        // [Patch]
        public string OffsetVid = "";
        public string OffsetPid = "";
        public int ScanTimeoutSec = 45;

        // [Asio]
        public string AsioDriver = "";
        public int AsioInputChannel = 0;
        public int AsioInput2Channel = 1;
        public int AsioOutputChannel = 0;
        public bool AsioSetRocksmithIni = true;

        public static string FilePath
        {
            get { return System.IO.Path.Combine(Program.AppDir, "NoCableLauncher.ini"); }
        }

        public static bool Exists { get { return File.Exists(FilePath); } }

        static bool B(string s, bool def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            s = s.Trim().ToLowerInvariant();
            return s == "1" || s == "true" || s == "yes";
        }

        static int I(string s, int def)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        static string Bs(bool b) { return b ? "1" : "0"; }

        public static Config Load()
        {
            Config c = new Config();
            IniFile f = new IniFile(FilePath);
            c.IsSteam = B(f.Get("Game", "Steam", null), c.IsSteam);
            c.GameExe = f.Get("Game", "GameExe", c.GameExe);
            c.GameDir = f.Get("Game", "GameDir", c.GameDir);

            c.Mode = string.Equals(f.Get("Mode", "AudioMode", "WASAPI"), "ASIO", StringComparison.OrdinalIgnoreCase)
                ? AudioMode.Asio : AudioMode.Wasapi;

            c.InputId = f.Get("Player1", "InputId", c.InputId);
            c.Vid = f.Get("Player1", "VID", c.Vid);
            c.Pid = f.Get("Player1", "PID", c.Pid);
            c.ManualIds = B(f.Get("Player1", "ManualIds", null), c.ManualIds);

            c.Multiplayer = B(f.Get("Player2", "Enabled", null), c.Multiplayer);
            c.Input2Id = f.Get("Player2", "InputId", c.Input2Id);
            c.Vid2 = f.Get("Player2", "VID", c.Vid2);
            c.Pid2 = f.Get("Player2", "PID", c.Pid2);
            c.ManualIds2 = B(f.Get("Player2", "ManualIds", null), c.ManualIds2);

            c.OutputId = f.Get("Devices", "OutputId", c.OutputId);
            c.HideOtherInputs = B(f.Get("Devices", "HideOtherInputs", null), c.HideOtherInputs);
            c.HideOtherOutputs = B(f.Get("Devices", "HideOtherOutputs", null), c.HideOtherOutputs);
            c.SetDefaultDevices = B(f.Get("Devices", "SetDefaultDevices", null), c.SetDefaultDevices);
            c.RestoreDelaySec = I(f.Get("Devices", "RestoreDelaySec", null), c.RestoreDelaySec);

            c.SetFormat = B(f.Get("Format", "SetFormat", null), c.SetFormat);
            c.SampleRate = I(f.Get("Format", "SampleRate", null), c.SampleRate);
            c.BitDepth = I(f.Get("Format", "BitDepth", null), c.BitDepth);

            c.OffsetVid = f.Get("Patch", "OffsetVID", c.OffsetVid);
            c.OffsetPid = f.Get("Patch", "OffsetPID", c.OffsetPid);
            c.ScanTimeoutSec = Math.Max(5, I(f.Get("Patch", "ScanTimeoutSec", null), c.ScanTimeoutSec));

            c.AsioDriver = f.Get("Asio", "Driver", c.AsioDriver);
            c.AsioInputChannel = I(f.Get("Asio", "InputChannel", null), c.AsioInputChannel);
            c.AsioInput2Channel = I(f.Get("Asio", "Input2Channel", null), c.AsioInput2Channel);
            c.AsioOutputChannel = I(f.Get("Asio", "OutputBaseChannel", null), c.AsioOutputChannel);
            c.AsioSetRocksmithIni = B(f.Get("Asio", "SetRocksmithIni", null), c.AsioSetRocksmithIni);
            return c;
        }

        public void Save()
        {
            IniFile f = new IniFile(FilePath);
            f.Set("Game", "Steam", Bs(IsSteam));
            f.Set("Game", "GameExe", GameExe);
            f.Set("Game", "GameDir", GameDir);

            f.Set("Mode", "AudioMode", Mode == AudioMode.Asio ? "ASIO" : "WASAPI");

            f.Set("Player1", "InputId", InputId);
            f.Set("Player1", "VID", Vid);
            f.Set("Player1", "PID", Pid);
            f.Set("Player1", "ManualIds", Bs(ManualIds));

            f.Set("Player2", "Enabled", Bs(Multiplayer));
            f.Set("Player2", "InputId", Input2Id);
            f.Set("Player2", "VID", Vid2);
            f.Set("Player2", "PID", Pid2);
            f.Set("Player2", "ManualIds", Bs(ManualIds2));

            f.Set("Devices", "OutputId", OutputId);
            f.Set("Devices", "HideOtherInputs", Bs(HideOtherInputs));
            f.Set("Devices", "HideOtherOutputs", Bs(HideOtherOutputs));
            f.Set("Devices", "SetDefaultDevices", Bs(SetDefaultDevices));
            f.Set("Devices", "RestoreDelaySec", RestoreDelaySec.ToString(CultureInfo.InvariantCulture));

            f.Set("Format", "SetFormat", Bs(SetFormat));
            f.Set("Format", "SampleRate", SampleRate.ToString(CultureInfo.InvariantCulture));
            f.Set("Format", "BitDepth", BitDepth.ToString(CultureInfo.InvariantCulture));

            f.Set("Patch", "OffsetVID", OffsetVid);
            f.Set("Patch", "OffsetPID", OffsetPid);
            f.Set("Patch", "ScanTimeoutSec", ScanTimeoutSec.ToString(CultureInfo.InvariantCulture));

            f.Set("Asio", "Driver", AsioDriver);
            f.Set("Asio", "InputChannel", AsioInputChannel.ToString(CultureInfo.InvariantCulture));
            f.Set("Asio", "Input2Channel", AsioInput2Channel.ToString(CultureInfo.InvariantCulture));
            f.Set("Asio", "OutputBaseChannel", AsioOutputChannel.ToString(CultureInfo.InvariantCulture));
            f.Set("Asio", "SetRocksmithIni", Bs(AsioSetRocksmithIni));
            f.Save();
        }
    }

    public static class Log
    {
        static readonly object sync = new object();
        static string path;

        public static void Init()
        {
            path = System.IO.Path.Combine(Program.AppDir, "NoCableLauncher.log");
            try
            {
                // keep previous run for diagnostics
                if (File.Exists(path))
                {
                    string old = path + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(path, old);
                }
            }
            catch { }
            Write("NoCableLauncher " + Program.Version + ", " + Environment.OSVersion + (Environment.Is64BitOperatingSystem ? " x64" : " x86"));
        }

        public static void Write(string msg)
        {
            if (path == null) return;
            lock (sync)
            {
                try
                {
                    File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + msg + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
        }
    }
}
