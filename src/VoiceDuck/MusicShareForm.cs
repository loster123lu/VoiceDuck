using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace VoiceDuck
{
    internal sealed class MusicShareForm : Form
    {
        private static readonly Color WindowColor = Color.FromArgb(17, 22, 31);
        private static readonly Color CardColor = Color.FromArgb(27, 35, 49);
        private static readonly Color TextColor = Color.FromArgb(239, 243, 250);
        private static readonly Color SubtleColor = Color.FromArgb(139, 151, 171);
        private static readonly Color AccentColor = Color.FromArgb(91, 140, 255);
        private static readonly Color GreenColor = Color.FromArgb(62, 207, 142);
        private static readonly Color OrangeColor = Color.FromArgb(255, 176, 84);

        private readonly AppSettings _settings;
        private readonly MusicShareAudioEngine _engine;
        private readonly VirtualAudioInstaller _driverInstaller;
        private readonly Action _settingsChanged;
        private readonly System.Windows.Forms.Timer _timer;
        private ComboBox _microphoneBox;
        private ComboBox _monitorBox;
        private TextBox _musicPathBox;
        private TrackBar _microphoneGain;
        private TrackBar _musicGain;
        private Label _microphoneGainLabel;
        private Label _musicGainLabel;
        private Label _driverStatus;
        private Label _shareStatus;
        private Label _timeLabel;
        private ProgressBar _microphoneMeter;
        private ProgressBar _musicMeter;
        private Button _installButton;
        private Button _uninstallButton;
        private Button _startButton;
        private Button _pauseButton;
        private Button _stopButton;
        private CheckBox _routingCheck;
        private bool _updating;
        private bool _driverActionRunning;
        private bool _ownerShuttingDown;

        public MusicShareForm(AppSettings settings, MusicShareAudioEngine engine, Action settingsChanged)
        {
            _settings = settings;
            _engine = engine;
            _settingsChanged = settingsChanged;
            _driverInstaller = new VirtualAudioInstaller();

            Text = "VoiceDuck · 通话音乐分享";
            ClientSize = new Size(790, 700);
            MinimumSize = new Size(806, 739);
            MaximumSize = MinimumSize;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = WindowColor;
            ForeColor = TextColor;
            Font = new Font("Segoe UI", 9.2f, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            BuildInterface();
            LoadSettings();
            RefreshDevices();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 250;
            _timer.Tick += TimerTick;
            _timer.Start();
            FormClosing += MusicShareFormClosing;
        }

        public void PrepareForOwnerShutdown()
        {
            _ownerShuttingDown = true;
            _timer.Stop();
        }

        private void BuildInterface()
        {
            Label title = CreateLabel("让对方听到你的音乐", 18.0f, FontStyle.Bold, TextColor);
            title.SetBounds(24, 18, 360, 34);
            Controls.Add(title);
            Label subtitle = CreateLabel("音乐单独送到耳机，麦克风＋音乐送到 CABLE Output；不会采集对方声音", 9.2f, FontStyle.Regular, SubtleColor);
            subtitle.SetBounds(26, 55, 730, 24);
            Controls.Add(subtitle);

            Panel driverCard = CreateCard(24, 88, 742, 118);
            Controls.Add(driverCard);
            Label driverTitle = CreateLabel("① 虚拟麦克风", 11.5f, FontStyle.Bold, TextColor);
            driverTitle.SetBounds(16, 12, 180, 25);
            driverCard.Controls.Add(driverTitle);
            _driverStatus = CreateLabel("正在检测内置 VB-CABLE…", 9.2f, FontStyle.Regular, SubtleColor);
            _driverStatus.SetBounds(18, 42, 445, 48);
            driverCard.Controls.Add(_driverStatus);
            _installButton = CreateButton("安装内置驱动", AccentColor, Color.White);
            _installButton.SetBounds(476, 18, 120, 36);
            _installButton.Click += delegate { BeginDriverAction(false); };
            driverCard.Controls.Add(_installButton);
            _uninstallButton = CreateButton("卸载", Color.FromArgb(48, 60, 77), TextColor);
            _uninstallButton.SetBounds(606, 18, 112, 36);
            _uninstallButton.Click += delegate { BeginDriverAction(true); };
            driverCard.Controls.Add(_uninstallButton);
            Label driverNote = CreateLabel("官方签名驱动 · 安装后不改系统默认设备 · 不会自动重启", 8.4f, FontStyle.Regular, SubtleColor);
            driverNote.SetBounds(478, 66, 242, 36);
            driverCard.Controls.Add(driverNote);

            Panel routeCard = CreateCard(24, 218, 742, 210);
            Controls.Add(routeCard);
            Label routeTitle = CreateLabel("② 选择输入、耳机和音乐", 11.5f, FontStyle.Bold, TextColor);
            routeTitle.SetBounds(16, 12, 300, 25);
            routeCard.Controls.Add(routeTitle);
            AddFieldLabel(routeCard, "你的麦克风", 18, 48);
            _microphoneBox = CreateComboBox();
            _microphoneBox.SetBounds(142, 44, 570, 30);
            routeCard.Controls.Add(_microphoneBox);
            AddFieldLabel(routeCard, "本地耳机/音箱", 18, 84);
            _monitorBox = CreateComboBox();
            _monitorBox.SetBounds(142, 80, 570, 30);
            routeCard.Controls.Add(_monitorBox);
            AddFieldLabel(routeCard, "要分享的音乐", 18, 120);
            _musicPathBox = new TextBox();
            _musicPathBox.ReadOnly = true;
            _musicPathBox.BackColor = Color.FromArgb(20, 27, 38);
            _musicPathBox.ForeColor = TextColor;
            _musicPathBox.BorderStyle = BorderStyle.FixedSingle;
            _musicPathBox.SetBounds(142, 117, 444, 28);
            routeCard.Controls.Add(_musicPathBox);
            Button browseButton = CreateButton("选择文件…", AccentColor, Color.White);
            browseButton.SetBounds(596, 115, 116, 31);
            browseButton.Click += BrowseMusic;
            routeCard.Controls.Add(browseButton);
            Label formatNote = CreateLabel("支持 MP3 / WAV / M4A / AAC / WMA / FLAC（使用 Windows 本机解码器）", 8.2f, FontStyle.Regular, SubtleColor);
            formatNote.SetBounds(142, 151, 570, 22);
            routeCard.Controls.Add(formatNote);
            Button refreshButton = CreateButton("刷新设备", Color.FromArgb(48, 60, 77), TextColor);
            refreshButton.SetBounds(18, 166, 110, 30);
            refreshButton.Click += delegate { RefreshDevices(); };
            routeCard.Controls.Add(refreshButton);
            Button soundSettingsButton = CreateButton("打开声音设置", Color.FromArgb(48, 60, 77), TextColor);
            soundSettingsButton.SetBounds(142, 166, 130, 30);
            soundSettingsButton.Click += OpenSoundSettings;
            routeCard.Controls.Add(soundSettingsButton);
            Label routingHelp = CreateLabel("微信/QQ：输入选 CABLE Output；输出继续选你的真实耳机", 8.8f, FontStyle.Bold, AccentColor);
            routingHelp.SetBounds(286, 169, 426, 24);
            routeCard.Controls.Add(routingHelp);

            Panel mixCard = CreateCard(24, 440, 742, 116);
            Controls.Add(mixCard);
            Label mixTitle = CreateLabel("③ 混音音量", 11.5f, FontStyle.Bold, TextColor);
            mixTitle.SetBounds(16, 10, 180, 25);
            mixCard.Controls.Add(mixTitle);
            AddGainSlider(mixCard, "麦克风", 18, 42, out _microphoneGain, out _microphoneGainLabel);
            AddGainSlider(mixCard, "音乐", 385, 42, out _musicGain, out _musicGainLabel);
            _microphoneGain.ValueChanged += GainChanged;
            _musicGain.ValueChanged += GainChanged;

            _routingCheck = new CheckBox();
            _routingCheck.Text = "我已确认：通话输入是 CABLE Output，通话输出仍是真实耳机";
            _routingCheck.ForeColor = TextColor;
            _routingCheck.BackColor = Color.Transparent;
            _routingCheck.SetBounds(28, 568, 520, 28);
            Controls.Add(_routingCheck);

            _startButton = CreateButton("开始分享", AccentColor, Color.White);
            _startButton.SetBounds(28, 605, 150, 42);
            _startButton.Click += StartSharing;
            Controls.Add(_startButton);
            _pauseButton = CreateButton("暂停音乐", Color.FromArgb(48, 60, 77), TextColor);
            _pauseButton.SetBounds(188, 605, 132, 42);
            _pauseButton.Click += delegate { _engine.TogglePause(); };
            Controls.Add(_pauseButton);
            _stopButton = CreateButton("停止分享", Color.FromArgb(91, 47, 55), Color.FromArgb(255, 210, 216));
            _stopButton.SetBounds(330, 605, 132, 42);
            _stopButton.Click += delegate { _engine.Stop(); };
            Controls.Add(_stopButton);

            _shareStatus = CreateLabel("尚未开始分享", 9.0f, FontStyle.Bold, SubtleColor);
            _shareStatus.SetBounds(482, 582, 280, 26);
            _shareStatus.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_shareStatus);
            _timeLabel = CreateLabel("00:00 / 00:00", 8.5f, FontStyle.Regular, SubtleColor);
            _timeLabel.SetBounds(482, 610, 280, 22);
            _timeLabel.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_timeLabel);
            _microphoneMeter = CreateMeter(482, 640, 132);
            _musicMeter = CreateMeter(626, 640, 132);
            Controls.Add(_microphoneMeter);
            Controls.Add(_musicMeter);
            Label meterLabels = CreateLabel("麦克风电平                         音乐电平", 7.8f, FontStyle.Regular, SubtleColor);
            meterLabels.SetBounds(482, 666, 278, 20);
            Controls.Add(meterLabels);
        }

        private void LoadSettings()
        {
            _updating = true;
            try
            {
                _settings.Normalize();
                _musicPathBox.Text = _settings.ShareMusicFile;
                _microphoneGain.Value = Clamp((int)Math.Round(_settings.ShareMicrophoneGain * 100), 0, 150);
                _musicGain.Value = Clamp((int)Math.Round(_settings.ShareMusicGain * 100), 0, 150);
                UpdateGainLabels();
            }
            finally { _updating = false; }
        }

        private void RefreshDevices()
        {
            try
            {
                List<AudioEndpointChoice> microphones = AudioEndpointCatalog.GetPhysicalCaptureEndpoints();
                List<AudioEndpointChoice> monitors = AudioEndpointCatalog.GetPhysicalRenderEndpoints();
                FillCombo(_microphoneBox, microphones, _settings.ShareMicrophoneDevice);
                FillCombo(_monitorBox, monitors, _settings.ShareMonitorDevice);
                VirtualCableStatus status = _driverInstaller.GetStatus();
                _driverStatus.Text = status.Message + (status.Ready ? "\r\n通话软件的麦克风请选择 “" + status.CaptureName + "”。" : String.Empty);
                _driverStatus.ForeColor = status.Ready ? GreenColor : OrangeColor;
                _installButton.Enabled = !_driverActionRunning && !status.Ready && _driverInstaller.EmbeddedPackageAvailable;
                _uninstallButton.Enabled = !_driverActionRunning && status.Installed;
            }
            catch (Exception exception)
            {
                _driverStatus.Text = "设备检测失败：" + exception.Message;
                _driverStatus.ForeColor = OrangeColor;
            }
        }

        private void BeginDriverAction(bool uninstall)
        {
            string callProcess;
            bool callDetected = CallSafetyInspector.HasPotentialCallAudioSession(
                _settings.TriggerApps,
                out callProcess);
            string action = uninstall ? "卸载" : "安装";
            string confirmationText;
            string confirmationTitle;
            if (callDetected)
            {
                confirmationTitle = "通话中" + action + "驱动确认";
                confirmationText =
                    "检测到通话应用正在使用音频（" + callProcess + ".exe）。\r\n\r\n" +
                    "预计总用时：约 10～30 秒。\r\n" +
                    "可能影响：Windows 写入驱动的约 3～10 秒内，通话声音可能短暂中断。\r\n\r\n" +
                    "VoiceDuck 会先完成哈希与签名校验，再请求管理员权限；不会修改系统默认麦克风或扬声器，也不会自动重启。\r\n\r\n" +
                    "为保护当前通话，默认选择“否”。仍要继续" + action + "吗？";
            }
            else
            {
                confirmationTitle = action + "内置虚拟音频驱动";
                confirmationText =
                    action + " VB-CABLE 需要管理员权限。\r\n\r\n" +
                    "预计总用时：约 10～30 秒；哈希与签名校验不会触碰音频设备，只有最后的驱动步骤可能让设备列表短暂刷新。\r\n\r\n" +
                    "VoiceDuck 不会修改系统默认音频设备，也不会自动重启。继续吗？";
            }
            DialogResult confirmation = MessageBox.Show(
                this,
                confirmationText,
                confirmationTitle,
                MessageBoxButtons.YesNo,
                callDetected ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes) return;

            _driverActionRunning = true;
            RefreshDevices();
            ThreadPool.QueueUserWorkItem(delegate
            {
                DriverActionResult result = uninstall ? _driverInstaller.Uninstall() : _driverInstaller.Install();
                if (IsDisposed) return;
                BeginInvoke(new Action(delegate
                {
                    _driverActionRunning = false;
                    RefreshDevices();
                    MessageBox.Show(
                        this,
                        result.Message,
                        result.Succeeded ? "驱动操作完成" : "驱动操作未完成",
                        MessageBoxButtons.OK,
                        result.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                }));
            });
        }

        private void BrowseMusic(object sender, EventArgs eventArgs)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择要在通话中分享的音乐";
                dialog.Filter = "音频文件|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac|所有文件|*.*";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _musicPathBox.Text = dialog.FileName;
                _settings.ShareMusicFile = dialog.FileName;
                SaveSettings();
            }
        }

        private void StartSharing(object sender, EventArgs eventArgs)
        {
            try
            {
                VirtualCableStatus cable = _driverInstaller.GetStatus();
                if (!cable.Ready) throw new InvalidOperationException(cable.Message);
                AudioEndpointChoice microphone = _microphoneBox.SelectedItem as AudioEndpointChoice;
                AudioEndpointChoice monitor = _monitorBox.SelectedItem as AudioEndpointChoice;
                if (microphone == null || monitor == null) throw new InvalidOperationException("请选择真实麦克风和本地耳机。");
                if (!File.Exists(_musicPathBox.Text)) throw new FileNotFoundException("请先选择一个本地音乐文件。");
                if (!_routingCheck.Checked)
                    throw new InvalidOperationException("请先确认微信/QQ 的输入与输出路由，避免对方听不到声音或产生回声。");

                _settings.ShareMicrophoneDevice = microphone.Id;
                _settings.ShareMonitorDevice = monitor.Id;
                _settings.ShareMusicFile = _musicPathBox.Text;
                _settings.ShareMicrophoneGain = _microphoneGain.Value / 100.0f;
                _settings.ShareMusicGain = _musicGain.Value / 100.0f;
                SaveSettings();
                _engine.Start(new MusicShareStartOptions
                {
                    MicrophoneEndpointId = microphone.Id,
                    MonitorEndpointId = monitor.Id,
                    MusicFilePath = _musicPathBox.Text,
                    MicrophoneGain = _settings.ShareMicrophoneGain,
                    MusicGain = _settings.ShareMusicGain
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "音乐分享无法启动", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void GainChanged(object sender, EventArgs eventArgs)
        {
            UpdateGainLabels();
            if (_updating) return;
            _settings.ShareMicrophoneGain = _microphoneGain.Value / 100.0f;
            _settings.ShareMusicGain = _musicGain.Value / 100.0f;
            _engine.UpdateGains(_settings.ShareMicrophoneGain, _settings.ShareMusicGain);
            SaveSettings();
        }

        private void TimerTick(object sender, EventArgs eventArgs)
        {
            MusicShareStatus status = _engine.GetStatus();
            _microphoneMeter.Value = PeakToMeter(status.MicrophonePeak);
            _musicMeter.Value = PeakToMeter(status.MusicPeak);
            _timeLabel.Text = FormatTime(status.Position) + " / " + FormatTime(status.Duration);
            if (!String.IsNullOrWhiteSpace(status.LastError))
            {
                _shareStatus.Text = "音频错误";
                _shareStatus.ForeColor = OrangeColor;
            }
            else if (status.TrackEnded)
            {
                _shareStatus.Text = "音乐已播完 · 麦克风仍在分享";
                _shareStatus.ForeColor = OrangeColor;
            }
            else if (status.Running)
            {
                _shareStatus.Text = status.Paused ? "音乐已暂停 · 麦克风仍在分享" : "正在分享 · " + status.TrackName;
                _shareStatus.ForeColor = GreenColor;
            }
            else
            {
                _shareStatus.Text = "尚未开始分享";
                _shareStatus.ForeColor = SubtleColor;
            }
            _startButton.Enabled = !status.Running;
            _pauseButton.Enabled = status.Running && !status.TrackEnded;
            _pauseButton.Text = status.Paused ? "继续音乐" : "暂停音乐";
            _stopButton.Enabled = status.Running;
        }

        private void OpenSoundSettings(object sender, EventArgs eventArgs)
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "无法打开声音设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void MusicShareFormClosing(object sender, FormClosingEventArgs eventArgs)
        {
            if (!_ownerShuttingDown && eventArgs.CloseReason == CloseReason.UserClosing)
            {
                eventArgs.Cancel = true;
                Hide();
            }
        }

        private void SaveSettings()
        {
            _settings.Normalize();
            SettingsStore.Save(_settings);
            if (_settingsChanged != null) _settingsChanged();
        }

        private void UpdateGainLabels()
        {
            _microphoneGainLabel.Text = _microphoneGain.Value + "%";
            _musicGainLabel.Text = _musicGain.Value + "%";
        }

        private static void FillCombo(ComboBox combo, IList<AudioEndpointChoice> items, string preferred)
        {
            combo.BeginUpdate();
            try
            {
                combo.Items.Clear();
                foreach (AudioEndpointChoice item in items) combo.Items.Add(item);
                int index = MusicShareCore.FindPreferredEndpointIndex(items, preferred);
                if (index >= 0) combo.SelectedIndex = index;
            }
            finally { combo.EndUpdate(); }
        }

        private static void AddFieldLabel(Control parent, string text, int x, int y)
        {
            Label label = CreateLabel(text, 9.0f, FontStyle.Regular, SubtleColor);
            label.SetBounds(x, y, 118, 24);
            parent.Controls.Add(label);
        }

        private static void AddGainSlider(
            Control parent,
            string text,
            int x,
            int y,
            out TrackBar track,
            out Label valueLabel)
        {
            Label label = CreateLabel(text, 8.8f, FontStyle.Regular, SubtleColor);
            label.SetBounds(x, y, 82, 22);
            parent.Controls.Add(label);
            valueLabel = CreateLabel("0%", 8.8f, FontStyle.Bold, TextColor);
            valueLabel.TextAlign = ContentAlignment.TopRight;
            valueLabel.SetBounds(x + 254, y, 62, 22);
            parent.Controls.Add(valueLabel);
            track = new TrackBar();
            track.Minimum = 0;
            track.Maximum = 150;
            track.TickStyle = TickStyle.None;
            track.BackColor = CardColor;
            track.SetBounds(x - 4, y + 24, 324, 38);
            parent.Controls.Add(track);
        }

        private static Panel CreateCard(int x, int y, int width, int height)
        {
            var panel = new Panel();
            panel.SetBounds(x, y, width, height);
            panel.BackColor = CardColor;
            return panel;
        }

        private static Label CreateLabel(string text, float size, FontStyle style, Color color)
        {
            var label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Font = new Font("Segoe UI", size, style, GraphicsUnit.Point);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            return label;
        }

        private static Button CreateButton(string text, Color background, Color foreground)
        {
            var button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = background;
            button.ForeColor = foreground;
            button.Cursor = Cursors.Hand;
            return button;
        }

        private static ComboBox CreateComboBox()
        {
            var combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.BackColor = Color.FromArgb(20, 27, 38);
            combo.ForeColor = TextColor;
            combo.FlatStyle = FlatStyle.Flat;
            return combo;
        }

        private static ProgressBar CreateMeter(int x, int y, int width)
        {
            var meter = new ProgressBar();
            meter.Minimum = 0;
            meter.Maximum = 100;
            meter.SetBounds(x, y, width, 18);
            return meter;
        }

        private static int PeakToMeter(float peak)
        {
            if (peak <= 0.00001f) return 0;
            double db = 20.0 * Math.Log10(peak);
            return Clamp((int)Math.Round((db + 60.0) / 60.0 * 100.0), 0, 100);
        }

        private static string FormatTime(TimeSpan time)
        {
            if (time < TimeSpan.Zero) time = TimeSpan.Zero;
            return ((int)time.TotalMinutes).ToString("00") + ":" + time.Seconds.ToString("00");
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
