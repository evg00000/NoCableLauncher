using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NoCableLauncher
{
    public class SettingsForm : Form
    {
        readonly Config cfg;
        public bool LaunchRequested { get; private set; }

        // Game
        CheckBox steamCheck;
        TextBox exeBox, dirBox;
        Button exeBrowse, dirBrowse, dirFind;
        RadioButton modeWasapi, modeAsio;

        // Devices
        ComboBox inCombo, outCombo, in2Combo, rateCombo, bitsCombo;
        TextBox vidBox, pidBox, vid2Box, pid2Box;
        CheckBox manualCheck, manual2Check, multiCheck, hideInCheck, hideOutCheck, defaultCheck, formatCheck;
        NumericUpDown restoreDelay;
        GroupBox p2Group;

        // ASIO
        ComboBox asioDriverCombo;
        NumericUpDown asioIn, asioIn2, asioOut;
        CheckBox asioIniCheck;
        Label asioStatus;

        // Advanced
        Label offsetLabel;
        NumericUpDown scanTimeout;
        ComboBox langCombo;

        List<AudioDevice> inputs, outputs;

        class NoneItem
        {
            readonly string text;
            public NoneItem(string t) { text = t; }
            public override string ToString() { return text; }
        }

        public SettingsForm(Config c)
        {
            cfg = c;
            Text = "NoCableLauncher " + Program.Version + Tr.T(" — настройки", " — settings");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 500);
            Font = new Font("Segoe UI", 9F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            TabControl tabs = new TabControl();
            tabs.SetBounds(8, 8, 544, 440);
            Controls.Add(tabs);

            tabs.TabPages.Add(BuildGameTab());
            tabs.TabPages.Add(BuildDevicesTab());
            tabs.TabPages.Add(BuildAsioTab());
            tabs.TabPages.Add(BuildAdvancedTab());

            Button run = Btn(this, Tr.T("Сохранить и запустить", "Save and launch"), 8, 458, 170);
            run.Click += delegate { if (Commit()) { LaunchRequested = true; DialogResult = DialogResult.OK; Close(); } };
            Button save = Btn(this, Tr.T("Сохранить", "Save"), 330, 458, 105);
            save.Click += delegate { if (Commit()) { DialogResult = DialogResult.OK; Close(); } };
            Button cancel = Btn(this, Tr.T("Отмена", "Cancel"), 447, 458, 105);
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;
            AcceptButton = run;

            LoadValues();
        }

        // ---------- small layout helpers ----------

        static Label Lbl(Control p, string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text; l.AutoSize = false;
            l.SetBounds(x, y + 3, w, 18);
            p.Controls.Add(l);
            return l;
        }

        static TextBox Txt(Control p, int x, int y, int w)
        {
            TextBox t = new TextBox();
            t.SetBounds(x, y, w, 23);
            p.Controls.Add(t);
            return t;
        }

        static Button Btn(Control p, string text, int x, int y, int w)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 27);
            p.Controls.Add(b);
            return b;
        }

        static CheckBox Chk(Control p, string text, int x, int y, int w)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.SetBounds(x, y, w, 22);
            p.Controls.Add(c);
            return c;
        }

        static ComboBox Cmb(Control p, int x, int y, int w, bool editable)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
            c.SetBounds(x, y, w, 23);
            p.Controls.Add(c);
            return c;
        }

        static NumericUpDown Num(Control p, int x, int y, int w, int min, int max)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min; n.Maximum = max;
            n.SetBounds(x, y, w, 23);
            p.Controls.Add(n);
            return n;
        }

        static Label Note(Control p, string text, int x, int y, int w, int h)
        {
            Label l = new Label();
            l.Text = text; l.AutoSize = false;
            l.ForeColor = SystemColors.GrayText;
            l.SetBounds(x, y, w, h);
            p.Controls.Add(l);
            return l;
        }

        // ---------- tabs ----------

        TabPage BuildGameTab()
        {
            TabPage t = new TabPage(Tr.T("Игра", "Game"));
            t.UseVisualStyleBackColor = true;

            steamCheck = Chk(t, Tr.T("Steam-версия (запуск через steam://rungameid/221680)", "Steam version (launch via steam://rungameid/221680)"), 12, 14, 500);
            Lbl(t, Tr.T("Exe игры:", "Game exe:"), 12, 46, 90);
            exeBox = Txt(t, 105, 44, 340);
            exeBrowse = Btn(t, "...", 452, 43, 70);
            steamCheck.CheckedChanged += delegate { exeBox.Enabled = exeBrowse.Enabled = !steamCheck.Checked; };
            exeBrowse.Click += delegate
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Filter = "Rocksmith2014.exe|Rocksmith2014.exe|Exe|*.exe";
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        exeBox.Text = d.FileName;
                        if (dirBox.Text.Length == 0) dirBox.Text = Path.GetDirectoryName(d.FileName);
                    }
                }
            };

            Lbl(t, Tr.T("Папка игры:", "Game folder:"), 12, 80, 90);
            dirBox = Txt(t, 105, 78, 260);
            dirFind = Btn(t, Tr.T("Найти", "Find"), 372, 77, 73);
            dirBrowse = Btn(t, "...", 452, 77, 70);
            dirFind.Click += delegate
            {
                string d = RsAsio.FindSteamGameDir();
                if (d != null) dirBox.Text = d;
                else MessageBox.Show(this, Tr.T("Не удалось найти Rocksmith 2014 в библиотеках Steam. Укажите папку вручную.", "Rocksmith 2014 was not found in your Steam libraries. Please choose the folder manually."),
                                     Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateAsioStatus();
            };
            dirBrowse.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.Description = Tr.T("Папка, где лежит Rocksmith2014.exe", "Folder that contains Rocksmith2014.exe");
                    if (d.ShowDialog(this) == DialogResult.OK) dirBox.Text = d.SelectedPath;
                    UpdateAsioStatus();
                }
            };
            Note(t, Tr.T("Папка нужна для режима RS_ASIO и правки Rocksmith.ini. Если пусто — ищется автоматически.", "Needed for RS_ASIO mode and Rocksmith.ini changes. If empty, it is detected automatically."), 105, 108, 420, 34);

            GroupBox g = new GroupBox();
            g.Text = Tr.T("Режим звука", "Audio mode");
            g.SetBounds(12, 150, 510, 190);
            t.Controls.Add(g);

            modeWasapi = new RadioButton();
            modeWasapi.Text = Tr.T("NoCable — патч памяти игры (WASAPI, любое устройство)", "NoCable — game memory patch (WASAPI, any device)");
            modeWasapi.SetBounds(14, 24, 480, 22);
            g.Controls.Add(modeWasapi);
            Note(g, Tr.T("Лончер подменяет ID кабеля Real Tone на ID вашего устройства. Перед запуском скрывает лишние ", "The launcher replaces the Real Tone Cable IDs with your device's IDs. Before the start it hides other ") +
                    Tr.T("звуковые устройства и выставляет частоту/разрядность, после выхода всё возвращает. ", "audio devices and sets the sample rate/bit depth, and restores everything on exit. ") +
                    Tr.T("RS_ASIO в этом режиме убирается из папки игры.", "In this mode RS_ASIO is removed from the game folder."), 32, 48, 465, 52);

            modeAsio = new RadioButton();
            modeAsio.Text = Tr.T("RS_ASIO — звук через ASIO-драйвер (только Steam-версия)", "RS_ASIO — audio through an ASIO driver (Steam version only)");
            modeAsio.SetBounds(14, 104, 480, 22);
            g.Controls.Add(modeAsio);
            Note(g, Tr.T("Ниже задержка, ничего не нужно отключать. Патч памяти не применяется. ", "Lower latency, nothing to disable. No memory patch is applied. ") +
                    Tr.T("Нужны файлы из релиза RS_ASIO (см. вкладку RS_ASIO).", "Requires the files from an RS_ASIO release (see the RS_ASIO tab)."), 32, 128, 465, 40);

            return t;
        }

        TabPage BuildDevicesTab()
        {
            TabPage t = new TabPage(Tr.T("Устройства", "Devices"));
            t.UseVisualStyleBackColor = true;

            Lbl(t, Tr.T("Вход (гитара):", "Input (guitar):"), 12, 14, 100);
            inCombo = Cmb(t, 115, 12, 407, false);
            Lbl(t, "VID:", 115, 44, 30);
            vidBox = Txt(t, 148, 42, 55);
            Lbl(t, "PID:", 215, 44, 30);
            pidBox = Txt(t, 248, 42, 55);
            manualCheck = Chk(t, Tr.T("Задать вручную", "Set manually"), 320, 42, 150);
            inCombo.SelectedIndexChanged += delegate { FillIds(inCombo, vidBox, pidBox, manualCheck); };
            manualCheck.CheckedChanged += delegate
            {
                vidBox.ReadOnly = pidBox.ReadOnly = !manualCheck.Checked;
                FillIds(inCombo, vidBox, pidBox, manualCheck);
            };

            Lbl(t, Tr.T("Выход (наушники):", "Output (phones):"), 12, 76, 110);
            outCombo = Cmb(t, 125, 74, 397, false);

            hideInCheck = Chk(t, Tr.T("Скрывать на время игры все остальные входы", "Hide all other inputs while the game runs"), 12, 106, 500);
            hideOutCheck = Chk(t, Tr.T("Скрывать на время игры все остальные выходы", "Hide all other outputs while the game runs"), 12, 128, 500);
            defaultCheck = Chk(t, Tr.T("Делать выбранные устройства устройствами по умолчанию", "Make the selected devices the default devices"), 12, 150, 500);

            formatCheck = Chk(t, Tr.T("Переключать формат:", "Set format:"), 12, 180, 150);
            rateCombo = Cmb(t, 165, 180, 90, false);
            rateCombo.Items.AddRange(new object[] { "44100", "48000", "96000" });
            Lbl(t, Tr.T("Гц", "Hz"), 260, 180, 25);
            bitsCombo = Cmb(t, 290, 180, 60, false);
            bitsCombo.Items.AddRange(new object[] { "16", "24", "32" });
            Lbl(t, Tr.T("бит", "bit"), 355, 180, 30);
            formatCheck.CheckedChanged += delegate { rateCombo.Enabled = bitsCombo.Enabled = formatCheck.Checked; };

            Lbl(t, Tr.T("Вернуть устройства через", "Restore devices after"), 12, 212, 160);
            restoreDelay = Num(t, 175, 210, 60, 0, 3600);
            Lbl(t, Tr.T("сек. после патча (0 — при выходе из игры)", "s after patching (0 = when the game exits)"), 240, 212, 290);

            p2Group = new GroupBox();
            p2Group.Text = Tr.T("Второй игрок", "Second player");
            p2Group.SetBounds(12, 244, 510, 152);
            t.Controls.Add(p2Group);

            multiCheck = Chk(p2Group, Tr.T("Мультиплеер (в игре: «Мультиплеер», затем Ctrl+M)", "Multiplayer (in game: \"Multiplayer\", then Ctrl+M)"), 12, 20, 480);
            Lbl(p2Group, Tr.T("Вход игрока 2:", "Player 2 input:"), 12, 52, 100);
            in2Combo = Cmb(p2Group, 115, 50, 380, false);
            Lbl(p2Group, "VID:", 115, 82, 30);
            vid2Box = Txt(p2Group, 148, 80, 55);
            Lbl(p2Group, "PID:", 215, 82, 30);
            pid2Box = Txt(p2Group, 248, 80, 55);
            manual2Check = Chk(p2Group, Tr.T("Задать вручную", "Set manually"), 320, 80, 150);
            in2Combo.SelectedIndexChanged += delegate { FillIds(in2Combo, vid2Box, pid2Box, manual2Check); };
            manual2Check.CheckedChanged += delegate
            {
                vid2Box.ReadOnly = pid2Box.ReadOnly = !manual2Check.Checked;
                FillIds(in2Combo, vid2Box, pid2Box, manual2Check);
            };
            multiCheck.CheckedChanged += delegate
            {
                in2Combo.Enabled = vid2Box.Enabled = pid2Box.Enabled = manual2Check.Enabled = multiCheck.Checked;
            };
            Btn(p2Group, Tr.T("Панель «Звук»", "Sound settings"), 12, 114, 140).Click += delegate { Process.Start("control", "mmsys.cpl,,1"); };
            Btn(p2Group, Tr.T("Обновить список", "Refresh list"), 160, 114, 140).Click += delegate { Commit(false); LoadDevices(); };

            return t;
        }

        TabPage BuildAsioTab()
        {
            TabPage t = new TabPage("RS_ASIO");
            t.UseVisualStyleBackColor = true;

            asioStatus = new Label();
            asioStatus.SetBounds(12, 12, 510, 70);
            t.Controls.Add(asioStatus);

            Lbl(t, Tr.T("ASIO-драйвер:", "ASIO driver:"), 12, 92, 100);
            asioDriverCombo = Cmb(t, 115, 90, 407, true);

            Lbl(t, Tr.T("Канал входа гитары:", "Guitar input channel:"), 12, 126, 160);
            asioIn = Num(t, 175, 124, 60, 0, 63);
            Note(t, Tr.T("0 = первый вход интерфейса, 1 = второй", "0 = first interface input, 1 = second"), 245, 127, 280, 18);

            Lbl(t, Tr.T("Канал входа игрока 2:", "Player 2 input channel:"), 12, 158, 160);
            asioIn2 = Num(t, 175, 156, 60, 0, 63);

            Lbl(t, Tr.T("Первый канал выхода:", "First output channel:"), 12, 190, 160);
            asioOut = Num(t, 175, 188, 60, 0, 63);

            asioIniCheck = Chk(t, Tr.T("Выставлять в Rocksmith.ini ExclusiveMode=1 и Win32UltraLowLatencyMode=1", "Set ExclusiveMode=1 and Win32UltraLowLatencyMode=1 in Rocksmith.ini"), 12, 222, 510);

            Note(t, Tr.T("Как установить: скачайте release-*.zip с github.com/mdias/rs_asio/releases и распакуйте ", "Setup: download release-*.zip from github.com/mdias/rs_asio/releases and extract ") +
                    Tr.T("avrt.dll, RS_ASIO.dll и RS_ASIO.ini в папку «RS_ASIO» рядом с лончером. Остальное лончер сделает сам: ", "avrt.dll, RS_ASIO.dll and RS_ASIO.ini into the \"RS_ASIO\" folder next to the launcher. The launcher does the rest: ") +
                    Tr.T("скопирует файлы в папку игры и пропишет настройки. Частоту 48 кГц и размер буфера задайте в панели ASIO-драйвера.", "it copies the files into the game folder and writes the settings. Set 48 kHz and the buffer size in the ASIO driver's control panel."),
                    12, 252, 510, 70);

            Btn(t, Tr.T("Открыть папку RS_ASIO", "Open RS_ASIO folder"), 12, 330, 170).Click += delegate
            {
                Directory.CreateDirectory(RsAsio.SourceDir);
                Process.Start("explorer.exe", "\"" + RsAsio.SourceDir + "\"");
                UpdateAsioStatus();
            };
            Btn(t, Tr.T("Убрать RS_ASIO из игры", "Remove RS_ASIO from game"), 190, 330, 170).Click += delegate
            {
                try
                {
                    string dir = CurrentGameDir();
                    if (dir == null) throw new Exception(Tr.T("Папка игры не найдена.", "Game folder not found."));
                    RsAsio.Disable(dir);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
                UpdateAsioStatus();
            };
            Btn(t, Tr.T("Журнал RS_ASIO", "RS_ASIO log"), 368, 330, 154).Click += delegate
            {
                string dir = CurrentGameDir();
                string log = dir != null ? Path.Combine(dir, "RS_ASIO-log.txt") : null;
                if (log != null && File.Exists(log)) Process.Start("notepad.exe", "\"" + log + "\"");
                else MessageBox.Show(this, Tr.T("Журнал появится после первого запуска игры с RS_ASIO.", "The log appears after the first game start with RS_ASIO."), Text);
            };
            return t;
        }

        TabPage BuildAdvancedTab()
        {
            TabPage t = new TabPage(Tr.T("Дополнительно", "Advanced"));
            t.UseVisualStyleBackColor = true;

            offsetLabel = Lbl(t, "", 12, 12, 360);
            Btn(t, Tr.T("Сбросить", "Reset"), 380, 9, 142).Click += delegate
            {
                cfg.OffsetVid = cfg.OffsetPid = "";
                UpdateOffsetLabel();
            };
            Note(t, Tr.T("Адрес находится автоматически при каждом запуске и кэшируется. Сбрасывать нужно только если что-то сломалось.", "The address is found automatically on every start and cached. Reset it only if something breaks."),
                 12, 38, 510, 34);

            Lbl(t, Tr.T("Ждать появления данных в памяти игры до", "Wait for the data in game memory up to"), 12, 82, 260);
            scanTimeout = Num(t, 275, 80, 60, 5, 300);
            Lbl(t, Tr.T("сек.", "s"), 340, 82, 40);

            Btn(t, Tr.T("Вернуть звуковые устройства сейчас", "Restore audio devices now"), 12, 120, 260).Click += delegate
            {
                AudioSetup.Restore();
                MessageBox.Show(this, AudioSetup.HasPendingRestore ? Tr.T("Не всё удалось вернуть — см. журнал.", "Not everything could be restored — see the log.") :
                                Tr.T("Готово: скрытые устройства, устройства по умолчанию и форматы возвращены.", "Done: hidden devices, default devices and formats have been restored."), Text);
            };
            Note(t, Tr.T("Если лончер когда-то закрылся аварийно, он сам вернёт устройства при следующем запуске. ", "If the launcher ever crashes, it restores the devices on the next start. ") +
                    Tr.T("То же делает команда NoCableLauncher.exe -restore.", "The command NoCableLauncher.exe -restore does the same."), 12, 152, 510, 34);

            Btn(t, Tr.T("Открыть журнал лончера", "Open launcher log"), 12, 196, 260).Click += delegate
            {
                string log = Path.Combine(Program.AppDir, "NoCableLauncher.log");
                if (File.Exists(log)) Process.Start("notepad.exe", "\"" + log + "\"");
            };

            Note(t, Tr.T("Подсказка: настройки открываются запуском с ключом -set (NCL_Settings.bat) ", "Tip: open the settings by starting with -set (NCL_Settings.bat) ") +
                    Tr.T("или если удерживать Shift при запуске лончера.", "or by holding Shift while starting the launcher."), 12, 240, 510, 34);

            Lbl(t, "Язык / Language:", 12, 288, 120);
            langCombo = Cmb(t, 135, 286, 200, false);
            langCombo.Items.AddRange(new object[] { Tr.T("Авто (как в Windows)", "Auto (Windows language)"), "Русский", "English" });
            Note(t, Tr.T("Применяется после перезапуска лончера.", "Takes effect after restarting the launcher."), 345, 289, 180, 34);
            return t;
        }

        // ---------- data ----------

        void LoadDevices()
        {
            try
            {
                inputs = CoreAudio.GetDevices(EDataFlow.Capture, DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged);
                outputs = CoreAudio.GetDevices(EDataFlow.Render, DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged);
            }
            catch (Exception ex)
            {
                inputs = new List<AudioDevice>();
                outputs = new List<AudioDevice>();
                MessageBox.Show(this, Tr.T("Не удалось получить список устройств: ", "Could not get the device list: ") + ex.Message, Text);
            }
            // active first
            inputs.Sort(delegate(AudioDevice a, AudioDevice b) { return a.IsActive == b.IsActive ? 0 : (a.IsActive ? -1 : 1); });
            outputs.Sort(delegate(AudioDevice a, AudioDevice b) { return a.IsActive == b.IsActive ? 0 : (a.IsActive ? -1 : 1); });

            FillCombo(inCombo, inputs, cfg.InputId, Tr.T("— не выбрано —", "— not selected —"));
            FillCombo(in2Combo, inputs, cfg.Input2Id, Tr.T("— не выбрано —", "— not selected —"));
            FillCombo(outCombo, outputs, cfg.OutputId, Tr.T("— не менять —", "— don't change —"));
        }

        static void FillCombo(ComboBox c, List<AudioDevice> list, string selectedId, string none)
        {
            c.BeginUpdate();
            c.Items.Clear();
            c.Items.Add(new NoneItem(none));
            int sel = 0;
            foreach (AudioDevice d in list)
            {
                int i = c.Items.Add(d);
                if (d.Id == selectedId) sel = i;
            }
            c.SelectedIndex = sel;
            c.EndUpdate();
        }

        static void FillIds(ComboBox c, TextBox vid, TextBox pid, CheckBox manual)
        {
            if (manual.Checked) return;
            AudioDevice d = c.SelectedItem as AudioDevice;
            vid.Text = d != null ? d.Vid : "0000";
            pid.Text = d != null ? d.Pid : "0000";
        }

        static string SelId(ComboBox c)
        {
            AudioDevice d = c.SelectedItem as AudioDevice;
            return d != null ? d.Id : "";
        }

        static void SelectText(ComboBox c, string value)
        {
            int i = c.Items.IndexOf(value);
            c.SelectedIndex = i >= 0 ? i : 1;
        }

        string CurrentGameDir()
        {
            Config tmp = new Config();
            tmp.IsSteam = steamCheck.Checked;
            tmp.GameExe = exeBox.Text.Trim();
            tmp.GameDir = dirBox.Text.Trim();
            return RsAsio.DetectGameDir(tmp);
        }

        void UpdateAsioStatus()
        {
            if (asioStatus == null) return;
            string dir = CurrentGameDir();
            string s = Tr.T("Файлы RS_ASIO рядом с лончером: ", "RS_ASIO files next to the launcher: ") + (RsAsio.SourceAvailable ? Tr.T("есть", "found") : Tr.T("НЕТ — папка ", "MISSING — folder ") + RsAsio.SourceDir) + "\n";
            s += Tr.T("Папка игры: ", "Game folder: ") + (dir ?? Tr.T("не найдена", "not found")) + "\n";
            if (dir != null) s += Tr.T("RS_ASIO в папке игры: ", "RS_ASIO in the game folder: ") + (RsAsio.IsInstalled(dir) ? Tr.T("установлен", "installed") : Tr.T("нет", "no"));
            asioStatus.Text = s;
        }

        void UpdateOffsetLabel()
        {
            offsetLabel.Text = Tr.T("Адрес ID кабеля в памяти: ", "Cable ID address in memory: ") +
                (cfg.OffsetVid.Length > 0 ? "0x" + cfg.OffsetVid : Tr.T("ещё не найден", "not found yet"));
        }

        void LoadValues()
        {
            steamCheck.Checked = cfg.IsSteam;
            exeBox.Text = cfg.GameExe;
            exeBox.Enabled = exeBrowse.Enabled = !cfg.IsSteam;
            dirBox.Text = cfg.GameDir;
            modeAsio.Checked = cfg.Mode == AudioMode.Asio;
            modeWasapi.Checked = cfg.Mode != AudioMode.Asio;

            LoadDevices();
            manualCheck.Checked = cfg.ManualIds;
            vidBox.ReadOnly = pidBox.ReadOnly = !cfg.ManualIds;
            if (cfg.ManualIds) { vidBox.Text = cfg.Vid; pidBox.Text = cfg.Pid; }
            else FillIds(inCombo, vidBox, pidBox, manualCheck);

            manual2Check.Checked = cfg.ManualIds2;
            vid2Box.ReadOnly = pid2Box.ReadOnly = !cfg.ManualIds2;
            if (cfg.ManualIds2) { vid2Box.Text = cfg.Vid2; pid2Box.Text = cfg.Pid2; }
            else FillIds(in2Combo, vid2Box, pid2Box, manual2Check);

            multiCheck.Checked = cfg.Multiplayer;
            in2Combo.Enabled = vid2Box.Enabled = pid2Box.Enabled = manual2Check.Enabled = cfg.Multiplayer;

            hideInCheck.Checked = cfg.HideOtherInputs;
            hideOutCheck.Checked = cfg.HideOtherOutputs;
            defaultCheck.Checked = cfg.SetDefaultDevices;
            formatCheck.Checked = cfg.SetFormat;
            SelectText(rateCombo, cfg.SampleRate.ToString());
            SelectText(bitsCombo, cfg.BitDepth.ToString());
            rateCombo.Enabled = bitsCombo.Enabled = cfg.SetFormat;
            restoreDelay.Value = Math.Max(0, Math.Min(3600, cfg.RestoreDelaySec));

            foreach (string d in RsAsio.GetDrivers()) asioDriverCombo.Items.Add(d);
            asioDriverCombo.Text = cfg.AsioDriver;
            if (asioDriverCombo.Text.Length == 0 && asioDriverCombo.Items.Count > 0)
            {
                // suggest a non-generic driver first (e.g. "UMC ASIO Driver" rather than ASIO4ALL)
                foreach (object o in asioDriverCombo.Items)
                {
                    string n = (string)o;
                    if (n.IndexOf("ASIO4ALL", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Generic", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Realtek", StringComparison.OrdinalIgnoreCase) < 0)
                    { asioDriverCombo.Text = n; break; }
                }
            }
            asioIn.Value = Math.Max(0, Math.Min(63, cfg.AsioInputChannel));
            asioIn2.Value = Math.Max(0, Math.Min(63, cfg.AsioInput2Channel));
            asioOut.Value = Math.Max(0, Math.Min(63, cfg.AsioOutputChannel));
            asioIniCheck.Checked = cfg.AsioSetRocksmithIni;
            UpdateAsioStatus();

            scanTimeout.Value = Math.Max(5, Math.Min(300, cfg.ScanTimeoutSec));
            langCombo.SelectedIndex = cfg.Language == "ru" ? 1 : (cfg.Language == "en" ? 2 : 0);
            UpdateOffsetLabel();
        }

        static bool IsHex4(string s)
        {
            if (s == null || s.Length != 4) return false;
            foreach (char ch in s)
                if (!Uri.IsHexDigit(ch)) return false;
            return true;
        }

        bool Commit() { return Commit(true); }

        bool Commit(bool validate)
        {
            if (validate)
            {
                if (!steamCheck.Checked && !File.Exists(exeBox.Text.Trim()))
                {
                    MessageBox.Show(this, Tr.T("Укажите путь к Rocksmith2014.exe или отметьте Steam-версию.", "Set the path to Rocksmith2014.exe or tick the Steam version."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                if (modeWasapi.Checked)
                {
                    if (!IsHex4(vidBox.Text.Trim()) || !IsHex4(pidBox.Text.Trim()))
                    {
                        MessageBox.Show(this, Tr.T("VID и PID — это по 4 шестнадцатеричные цифры (например 1397 и 0507).", "VID and PID are 4 hex digits each (for example 1397 and 0507)."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                    if (SelId(inCombo).Length == 0 && !manualCheck.Checked)
                    {
                        MessageBox.Show(this, Tr.T("Выберите входное устройство, к которому подключена гитара.", "Select the input device your guitar is connected to."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                    if (multiCheck.Checked && vidBox.Text.Trim() == vid2Box.Text.Trim() && pidBox.Text.Trim() == pid2Box.Text.Trim())
                        MessageBox.Show(this, Tr.T("У игроков 1 и 2 одинаковые VID/PID — мультиплеер так работать не будет.", "Players 1 and 2 have the same VID/PID — multiplayer will not work like this."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else if (asioDriverCombo.Text.Trim().Length == 0)
                {
                    MessageBox.Show(this, Tr.T("Выберите ASIO-драйвер.", "Select an ASIO driver."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            cfg.IsSteam = steamCheck.Checked;
            cfg.GameExe = exeBox.Text.Trim();
            cfg.GameDir = dirBox.Text.Trim();
            cfg.Mode = modeAsio.Checked ? AudioMode.Asio : AudioMode.Wasapi;

            cfg.InputId = SelId(inCombo);
            cfg.Vid = vidBox.Text.Trim().ToUpperInvariant();
            cfg.Pid = pidBox.Text.Trim().ToUpperInvariant();
            cfg.ManualIds = manualCheck.Checked;
            cfg.OutputId = SelId(outCombo);

            cfg.Multiplayer = multiCheck.Checked;
            cfg.Input2Id = SelId(in2Combo);
            cfg.Vid2 = vid2Box.Text.Trim().ToUpperInvariant();
            cfg.Pid2 = pid2Box.Text.Trim().ToUpperInvariant();
            cfg.ManualIds2 = manual2Check.Checked;

            cfg.HideOtherInputs = hideInCheck.Checked;
            cfg.HideOtherOutputs = hideOutCheck.Checked;
            cfg.SetDefaultDevices = defaultCheck.Checked;
            cfg.SetFormat = formatCheck.Checked;
            int v;
            if (rateCombo.SelectedItem != null && int.TryParse((string)rateCombo.SelectedItem, out v)) cfg.SampleRate = v;
            if (bitsCombo.SelectedItem != null && int.TryParse((string)bitsCombo.SelectedItem, out v)) cfg.BitDepth = v;
            cfg.RestoreDelaySec = (int)restoreDelay.Value;

            cfg.AsioDriver = asioDriverCombo.Text.Trim();
            cfg.AsioInputChannel = (int)asioIn.Value;
            cfg.AsioInput2Channel = (int)asioIn2.Value;
            cfg.AsioOutputChannel = (int)asioOut.Value;
            cfg.AsioSetRocksmithIni = asioIniCheck.Checked;

            cfg.ScanTimeoutSec = (int)scanTimeout.Value;
            cfg.Language = langCombo.SelectedIndex == 1 ? "ru" : (langCombo.SelectedIndex == 2 ? "en" : "auto");

            if (validate)
            {
                try { cfg.Save(); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, Tr.T("Не удалось сохранить настройки: ", "Could not save the settings: ") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
            return true;
        }
    }
}
