using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NoCableLauncher
{
    /// <summary>
    /// Prepares Windows audio for the game (hides foreign endpoints, sets defaults and sample format)
    /// and restores everything afterwards. Every change is journaled to a state file before it is made,
    /// so a crash or a killed launcher is repaired on the next start (or with "-restore").
    /// </summary>
    public static class AudioSetup
    {
        static string StatePath
        {
            get { return Path.Combine(Program.AppDir, "NoCableLauncher.state"); }
        }

        public static bool HasPendingRestore { get { return File.Exists(StatePath); } }

        static void Journal(string line)
        {
            File.AppendAllText(StatePath, line + Environment.NewLine, Encoding.UTF8);
        }

        static readonly ERole[] Roles = { ERole.Console, ERole.Multimedia, ERole.Communications };

        public static void Prepare(Config cfg, bool hidePlayer2)
        {
            if (HasPendingRestore) Restore();

            List<AudioDevice> inputs = CoreAudio.GetDevices(EDataFlow.Capture, DeviceState.Active);
            List<AudioDevice> outputs = CoreAudio.GetDevices(EDataFlow.Render, DeviceState.Active);

            foreach (AudioDevice d in inputs) Log.Write(Tr.T("Вход:  ", "Input:  ") + d + "  id=" + d.Id);
            foreach (AudioDevice d in outputs) Log.Write(Tr.T("Выход: ", "Output: ") + d + "  id=" + d.Id);

            bool inputPresent = cfg.InputId.Length > 0 && inputs.Exists(delegate(AudioDevice d) { return d.Id == cfg.InputId; });
            bool outputPresent = cfg.OutputId.Length > 0 && outputs.Exists(delegate(AudioDevice d) { return d.Id == cfg.OutputId; });

            if (cfg.InputId.Length > 0 && !inputPresent)
                Log.Write(Tr.T("ВНИМАНИЕ: выбранный вход не найден среди активных — другие входы скрываться не будут.", "WARNING: the selected input is not active — other inputs will not be hidden."));
            if (cfg.OutputId.Length > 0 && !outputPresent)
                Log.Write(Tr.T("ВНИМАНИЕ: выбранный выход не найден среди активных — другие выходы скрываться не будут.", "WARNING: the selected output is not active — other outputs will not be hidden."));

            // 1. Remember defaults
            foreach (ERole role in Roles)
            {
                string r = CoreAudio.GetDefaultId(EDataFlow.Render, role);
                if (r != null) Journal("default|0|" + (int)role + "|" + r);
                string c = CoreAudio.GetDefaultId(EDataFlow.Capture, role);
                if (c != null) Journal("default|1|" + (int)role + "|" + c);
            }

            // 2. Hide everything that is not ours
            if (cfg.HideOtherInputs && inputPresent)
            {
                foreach (AudioDevice d in inputs)
                {
                    bool keep = d.Id == cfg.InputId || (cfg.Multiplayer && !hidePlayer2 && d.Id == cfg.Input2Id);
                    if (!keep) Hide(d);
                }
            }
            else if (hidePlayer2 && cfg.Input2Id.Length > 0)
            {
                AudioDevice p2 = inputs.Find(delegate(AudioDevice d) { return d.Id == cfg.Input2Id; });
                if (p2 != null) Hide(p2);
            }

            if (cfg.HideOtherOutputs && outputPresent)
            {
                foreach (AudioDevice d in outputs)
                    if (d.Id != cfg.OutputId) Hide(d);
            }

            // 3. Defaults -> our devices
            if (cfg.SetDefaultDevices)
            {
                foreach (ERole role in Roles)
                {
                    if (inputPresent) Try("default capture", delegate { CoreAudio.SetDefault(cfg.InputId, role); });
                    if (outputPresent) Try("default render", delegate { CoreAudio.SetDefault(cfg.OutputId, role); });
                }
            }

            // 4. Sample rate / bit depth
            if (cfg.SetFormat)
            {
                if (inputPresent) ApplyFormat(cfg.InputId, cfg.SampleRate, cfg.BitDepth, Tr.T("вход", "input"));
                if (outputPresent) ApplyFormat(cfg.OutputId, cfg.SampleRate, cfg.BitDepth, Tr.T("выход", "output"));
                if (cfg.Multiplayer && cfg.Input2Id.Length > 0 && cfg.Input2Id != cfg.InputId)
                    ApplyFormat(cfg.Input2Id, cfg.SampleRate, cfg.BitDepth, Tr.T("вход игрока 2", "player 2 input"));
            }
        }

        delegate void Action0();

        static void Try(string what, Action0 a)
        {
            try { a(); }
            catch (Exception ex) { Log.Write(Tr.T("Ошибка (", "Error (") + what + "): " + ex.Message); }
        }

        static void Hide(AudioDevice d)
        {
            Journal("hidden|" + d.Id);
            try
            {
                CoreAudio.SetVisibility(d.Id, false);
                Log.Write(Tr.T("Скрыто: ", "Hidden: ") + d.Name);
            }
            catch (Exception ex) { Log.Write(Tr.T("Не удалось скрыть ", "Could not hide ") + d.Name + ": " + ex.Message); }
        }

        public static void ShowDevice(string id)
        {
            try { CoreAudio.SetVisibility(id, true); }
            catch (Exception ex) { Log.Write(Tr.T("Не удалось вернуть устройство: ", "Could not restore device: ") + ex.Message); }
        }

        static void ApplyFormat(string id, int rate, int bits, string label)
        {
            try
            {
                WaveFormat cur = CoreAudio.GetDeviceFormat(id);
                WaveFormat curMix = CoreAudio.GetMixFormat(id);
                if (cur == null)
                {
                    Log.Write(Tr.T("Формат (", "Format (") + label + Tr.T("): не удалось прочитать текущий формат, пропускаю", "): could not read the current format, skipping"));
                    return;
                }
                Log.Write(Tr.T("Формат (", "Format (") + label + Tr.T("): сейчас ", "): current ") + cur);
                if (cur.SampleRate == rate && cur.ValidBits == bits)
                {
                    Log.Write(Tr.T("Формат (", "Format (") + label + Tr.T("): уже нужный", "): already correct"));
                    return;
                }

                int container = bits;
                if (cur.ValidBits == bits) container = cur.ContainerBits;   // keep driver's container layout
                int channels = cur.Channels;
                int mask = cur.ChannelMask;

                WaveFormat endpoint = WaveFormat.Create(channels, rate, container, bits, mask, false);
                WaveFormat mix = WaveFormat.Create(channels, rate, 32, 32, mask, true);

                Journal("format|" + id + "|" + cur.ToHex() + "|" + (curMix != null ? curMix.ToHex() : ""));
                CoreAudio.SetDeviceFormat(id, endpoint, mix);

                WaveFormat now = CoreAudio.GetDeviceFormat(id);
                Log.Write(Tr.T("Формат (", "Format (") + label + Tr.T("): установлен ", "): set to ") + (now != null ? now.ToString() : "?"));

                if (now != null && (now.SampleRate != rate || now.ValidBits != bits) && container == 24)
                {
                    // Some drivers only accept 24-in-32 container. Try that.
                    endpoint = WaveFormat.Create(channels, rate, 32, 24, mask, false);
                    CoreAudio.SetDeviceFormat(id, endpoint, mix);
                    now = CoreAudio.GetDeviceFormat(id);
                    Log.Write(Tr.T("Формат (", "Format (") + label + Tr.T("): повтор 24-в-32 -> ", "): retry with 24-in-32 -> ") + (now != null ? now.ToString() : "?"));
                }
            }
            catch (Exception ex)
            {
                Log.Write(Tr.T("Формат (", "Format (") + label + Tr.T("): ошибка ", "): error ") + ex.Message + Tr.T(" — устройство поддерживает ", " — does the device support ") + rate + Tr.T(" Гц / ", " Hz / ") + bits + Tr.T(" бит?", " bit?"));
            }
        }

        /// <summary>Undo everything listed in the state file. Safe to call repeatedly.</summary>
        public static void Restore()
        {
            if (!File.Exists(StatePath)) return;
            Log.Write(Tr.T("Восстанавливаю звуковые настройки...", "Restoring audio settings..."));
            string[] lines;
            try { lines = File.ReadAllLines(StatePath); }
            catch (Exception ex) { Log.Write(Tr.T("Не удалось прочитать state: ", "Could not read state file: ") + ex.Message); return; }

            // visibility first, otherwise defaults/formats can't be applied to hidden devices
            foreach (string l in lines)
            {
                string[] p = l.Split('|');
                if (p.Length >= 2 && p[0] == "hidden") ShowDevice(p[1]);
            }
            // formats in reverse order (the first saved one is the original)
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string[] p = lines[i].Split('|');
                if (p.Length >= 3 && p[0] == "format")
                {
                    string id = p[1];
                    WaveFormat ep = WaveFormat.FromHex(p[2]);
                    WaveFormat mix = p.Length >= 4 ? WaveFormat.FromHex(p[3]) : null;
                    if (ep != null)
                        Try("restore format", delegate { CoreAudio.SetDeviceFormat(id, ep, mix); });
                }
            }
            // defaults: only the first record per flow/role is the original
            HashSet<string> done = new HashSet<string>();
            foreach (string l in lines)
            {
                string[] p = l.Split('|');
                if (p.Length >= 4 && p[0] == "default")
                {
                    string key = p[1] + "|" + p[2];
                    if (done.Contains(key)) continue;
                    done.Add(key);
                    int role;
                    if (!int.TryParse(p[2], out role)) continue;
                    string id = p[3];
                    Try("restore default", delegate { CoreAudio.SetDefault(id, (ERole)role); });
                }
            }

            try { File.Delete(StatePath); } catch { }
            Log.Write(Tr.T("Звуковые настройки восстановлены.", "Audio settings restored."));
        }

        /// <summary>Make Player 2 input visible again (multiplayer, after Ctrl+M).</summary>
        public static void RevealPlayer2(Config cfg)
        {
            if (cfg.Input2Id.Length > 0) ShowDevice(cfg.Input2Id);
        }
    }
}
