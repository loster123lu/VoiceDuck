using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace VoiceDuck
{
    internal sealed class AppChoice
    {
        public string ProcessName { get; set; }
        public string Text { get; set; }
        public override string ToString() { return Text; }
    }

    internal sealed class MainForm : Form
    {
        private static readonly Color WindowColor = Color.FromArgb(17, 22, 31);
        private static readonly Color CardColor = Color.FromArgb(27, 35, 49);
        private static readonly Color SubtleColor = Color.FromArgb(139, 151, 171);
        private static readonly Color TextColor = Color.FromArgb(239, 243, 250);
        private static readonly Color AccentColor = Color.FromArgb(91, 140, 255);
        private static readonly Color GreenColor = Color.FromArgb(62, 207, 142);
        private static readonly Color OrangeColor = Color.FromArgb(255, 176, 84);

        private readonly AppSettings _settings;
        private readonly AudioEngineService _service;
        private readonly MusicShareAudioEngine _musicShareService;
        private readonly HearingProtectionService _hearingProtectionService;
        private readonly IDefaultMicrophoneSwitcher _microphoneSwitcher;
        private readonly bool _ownsMicrophoneSwitcher;
        private readonly bool _startHidden;
        private readonly Timer _uiTimer;
        private NotifyIcon _trayIcon;
        private Label _statusBadge;
        private Label _statusDetail;
        private Button _toggleButton;
        private Button _musicShareButton;
        private Button _hearingProtectionButton;
        private MusicShareForm _musicShareForm;
        private HearingProtectionForm _hearingProtectionForm;
        private CheckedListBox _triggerList;
        private CheckedListBox _targetList;
        private CheckBox _duckAllCheck;
        private CheckBox _startupCheck;
        private CheckBox _minimizeCheck;
        private ComboBox _presetBox;
        private TrackBar _duckTrack;
        private TrackBar _thresholdTrack;
        private TrackBar _delayTrack;
        private TrackBar _holdTrack;
        private TrackBar _attackTrack;
        private TrackBar _releaseTrack;
        private Label _duckValue;
        private Label _thresholdValue;
        private Label _delayValue;
        private Label _holdValue;
        private Label _attackValue;
        private Label _releaseValue;
        private bool _updatingControls;
        private bool _allowExit;
        private int _refreshTicks;
        private string _sessionSignature = String.Empty;
        private string _startupMicrophoneWarning = String.Empty;

        public MainForm(AppSettings settings, bool startHidden)
            : this(settings, startHidden, null)
        {
        }

        internal MainForm(
            AppSettings settings,
            bool startHidden,
            IDefaultMicrophoneSwitcher microphoneSwitcher)
        {
            _settings = settings;
            _startHidden = startHidden;
            _service = new AudioEngineService(settings);
            _musicShareService = new MusicShareAudioEngine(_service.GetCallActivity);
            _hearingProtectionService = new HearingProtectionService(settings);
            if (microphoneSwitcher == null)
            {
                _microphoneSwitcher = new DefaultMicrophoneSwitcher(
                    new DefaultCaptureEndpointController(),
                    SettingsStore.MicrophoneRestorePath);
                _ownsMicrophoneSwitcher = true;
                MicrophoneRouteResult recovery = _microphoneSwitcher.Restore();
                if (!recovery.Succeeded) _startupMicrophoneWarning = recovery.Message;
            }
            else
            {
                _microphoneSwitcher = microphoneSwitcher;
                _ownsMicrophoneSwitcher = false;
            }

            Text = "VoiceDuck · 智能语音闪避";
            ClientSize = new Size(930, 700);
            MinimumSize = new Size(946, 739);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = WindowColor;
            ForeColor = TextColor;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            try
            {
                using (Icon executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    if (executableIcon != null) Icon = (Icon)executableIcon.Clone();
            }
            catch { }

            BuildInterface();
            BuildTrayIcon();
            LoadSettingsIntoControls();

            _triggerList.ItemCheck += TriggerListItemCheck;
            _targetList.ItemCheck += TargetListItemCheck;

            _service.Start();
            _hearingProtectionService.Start();
            _service.RequestRefresh();

            _uiTimer = new Timer();
            _uiTimer.Interval = 250;
            _uiTimer.Tick += UiTimerTick;
            _uiTimer.Start();

            Shown += delegate
            {
                RefreshApplicationLists(true);
                if (!String.IsNullOrWhiteSpace(_startupMicrophoneWarning))
                    _trayIcon.ShowBalloonTip(
                        5000,
                        "默认麦克风恢复未完成",
                        _startupMicrophoneWarning + " 请打开声音设置手动选择真实麦克风。",
                        ToolTipIcon.Warning);
                if (_startHidden) BeginInvoke(new Action(Hide));
            };
            FormClosing += MainFormClosing;
            FormClosed += delegate
            {
                _uiTimer.Stop();
                _trayIcon.Visible = false;
                if (_musicShareForm != null)
                {
                    _musicShareForm.PrepareForOwnerShutdown();
                    _musicShareForm.Close();
                    _musicShareForm.Dispose();
                    _musicShareForm = null;
                }
                else
                {
                    _microphoneSwitcher.Restore();
                }
                if (_hearingProtectionForm != null)
                {
                    _hearingProtectionForm.PrepareForOwnerShutdown();
                    _hearingProtectionForm.Close();
                    _hearingProtectionForm.Dispose();
                    _hearingProtectionForm = null;
                }
                _musicShareService.Dispose();
                _hearingProtectionService.Dispose();
                _service.Dispose();
                if (_ownsMicrophoneSwitcher)
                {
                    IDisposable disposable = _microphoneSwitcher as IDisposable;
                    if (disposable != null) disposable.Dispose();
                }
            };
        }

        public void Shutdown()
        {
            _allowExit = true;
            Close();
        }

        private void BuildInterface()
        {
            var header = CreateCard(new Rectangle(0, 0, ClientSize.Width, 94), WindowColor);
            Controls.Add(header);

            PictureBox logo = new PictureBox();
            logo.BackColor = Color.Transparent;
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            if (Icon != null) logo.Image = Icon.ToBitmap();
            logo.SetBounds(24, 22, 50, 50);
            header.Controls.Add(logo);

            Label title = CreateLabel("VoiceDuck", 19.0f, FontStyle.Bold, TextColor);
            title.SetBounds(88, 17, 220, 32);
            header.Controls.Add(title);
            Label subtitle = CreateLabel("对方讲话时，自动为音乐让路", 9.5f, FontStyle.Regular, SubtleColor);
            subtitle.SetBounds(90, 51, 300, 24);
            header.Controls.Add(subtitle);

            _hearingProtectionButton = CreateButton("音量保护", Color.FromArgb(49, 61, 80), TextColor);
            _hearingProtectionButton.SetBounds(402, 28, 110, 36);
            _hearingProtectionButton.Click += delegate { ShowHearingProtection(); };
            header.Controls.Add(_hearingProtectionButton);

            _musicShareButton = CreateButton("通话音乐分享", Color.FromArgb(49, 61, 80), TextColor);
            _musicShareButton.SetBounds(520, 28, 120, 36);
            _musicShareButton.Click += delegate { ShowMusicShare(); };
            header.Controls.Add(_musicShareButton);

            _statusBadge = CreateLabel("已暂停", 9.5f, FontStyle.Bold, SubtleColor);
            _statusBadge.TextAlign = ContentAlignment.MiddleCenter;
            _statusBadge.BackColor = Color.FromArgb(38, 45, 57);
            _statusBadge.SetBounds(650, 28, 76, 36);
            header.Controls.Add(_statusBadge);

            _toggleButton = CreateButton("开始自动闪避", AccentColor, Color.White);
            _toggleButton.SetBounds(738, 23, 165, 46);
            _toggleButton.Click += delegate
            {
                _settings.Enabled = !_settings.Enabled;
                ApplySettings();
                UpdateToggleAppearance();
            };
            header.Controls.Add(_toggleButton);

            Panel triggerCard = CreateCard(new Rectangle(24, 101, 430, 254), CardColor);
            Controls.Add(triggerCard);
            AddSectionHeader(triggerCard, "① 通话软件", "这些应用发声时触发", "对方讲话");
            _triggerList = CreateCheckedList();
            _triggerList.SetBounds(16, 69, 398, 166);
            triggerCard.Controls.Add(_triggerList);

            Panel targetCard = CreateCard(new Rectangle(468, 101, 438, 254), CardColor);
            Controls.Add(targetCard);
            AddSectionHeader(targetCard, "② 音乐与背景声", "只降低你勾选的应用", "被降低");
            _targetList = CreateCheckedList();
            _targetList.SetBounds(16, 69, 406, 130);
            targetCard.Controls.Add(_targetList);

            _duckAllCheck = CreateCheckBox("降低所有非通话应用（不建议浏览器通话时使用）");
            _duckAllCheck.SetBounds(19, 208, 343, 28);
            _duckAllCheck.CheckedChanged += delegate
            {
                if (_updatingControls) return;
                _settings.DuckAllOtherAudio = _duckAllCheck.Checked;
                _targetList.Enabled = !_duckAllCheck.Checked;
                ApplySettings();
            };
            targetCard.Controls.Add(_duckAllCheck);

            Button refreshButton = CreateButton("刷新", Color.FromArgb(49, 61, 80), TextColor);
            refreshButton.SetBounds(363, 207, 59, 29);
            refreshButton.Click += delegate
            {
                _service.RequestRefresh();
                _sessionSignature = String.Empty;
                _refreshTicks = 7;
            };
            targetCard.Controls.Add(refreshButton);

            Panel tuningCard = CreateCard(new Rectangle(24, 368, 882, 250), CardColor);
            Controls.Add(tuningCard);
            Label tuningTitle = CreateLabel("声音行为", 12.0f, FontStyle.Bold, TextColor);
            tuningTitle.SetBounds(17, 14, 120, 25);
            tuningCard.Controls.Add(tuningTitle);

            Label presetLabel = CreateLabel("预设", 9.0f, FontStyle.Regular, SubtleColor);
            presetLabel.SetBounds(633, 16, 42, 22);
            tuningCard.Controls.Add(presetLabel);
            _presetBox = new ComboBox();
            _presetBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _presetBox.FlatStyle = FlatStyle.Flat;
            _presetBox.BackColor = Color.FromArgb(41, 51, 68);
            _presetBox.ForeColor = TextColor;
            _presetBox.Items.AddRange(new object[] { "均衡（推荐）", "游戏语音", "会议专注", "柔和音乐", "自定义" });
            _presetBox.SetBounds(676, 12, 187, 28);
            _presetBox.SelectedIndexChanged += PresetChanged;
            tuningCard.Controls.Add(_presetBox);

            _duckTrack = AddSlider(tuningCard, 18, 54, "音乐保留音量", 5, 80, 25, out _duckValue);
            _thresholdTrack = AddSlider(tuningCard, 302, 54, "触发阈值", -65, -15, -42, out _thresholdValue);
            _delayTrack = AddSlider(tuningCard, 586, 54, "防提示音延迟", 50, 800, 180, out _delayValue);
            _holdTrack = AddSlider(tuningCard, 18, 144, "断句保持", 300, 4000, 1400, out _holdValue);
            _attackTrack = AddSlider(tuningCard, 302, 144, "降低速度", 50, 1000, 160, out _attackValue);
            _releaseTrack = AddSlider(tuningCard, 586, 144, "恢复速度", 200, 3000, 850, out _releaseValue);

            _duckTrack.ValueChanged += SliderChanged;
            _thresholdTrack.ValueChanged += SliderChanged;
            _delayTrack.ValueChanged += SliderChanged;
            _holdTrack.ValueChanged += SliderChanged;
            _attackTrack.ValueChanged += SliderChanged;
            _releaseTrack.ValueChanged += SliderChanged;

            _statusDetail = CreateLabel("正在初始化音频会话…", 9.0f, FontStyle.Regular, SubtleColor);
            _statusDetail.SetBounds(26, 632, 525, 28);
            Controls.Add(_statusDetail);

            _startupCheck = CreateCheckBox("开机启动");
            _startupCheck.SetBounds(612, 632, 103, 28);
            _startupCheck.CheckedChanged += StartupCheckChanged;
            Controls.Add(_startupCheck);

            _minimizeCheck = CreateCheckBox("关闭时最小化到托盘");
            _minimizeCheck.SetBounds(720, 632, 186, 28);
            _minimizeCheck.CheckedChanged += delegate
            {
                if (_updatingControls) return;
                _settings.MinimizeToTray = _minimizeCheck.Checked;
                ApplySettings();
            };
            Controls.Add(_minimizeCheck);

            Label footnote = CreateLabel(
                "自动闪避无需驱动 · 音量仅在本机调节",
                8.5f,
                FontStyle.Regular,
                Color.FromArgb(91, 103, 122));
            footnote.SetBounds(26, 663, 500, 22);
            Controls.Add(footnote);
        }

        private void BuildTrayIcon()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("显示 VoiceDuck", null, delegate { ShowMainWindow(); });
            menu.Items.Add("开启 / 暂停", null, delegate
            {
                _settings.Enabled = !_settings.Enabled;
                ApplySettings();
                UpdateToggleAppearance();
            });
            menu.Items.Add("通话音乐分享", null, delegate { ShowMusicShare(); });
            menu.Items.Add("耳机音量保护", null, delegate { ShowHearingProtection(); });
            menu.Items.Add("退出并恢复音量/麦克风", null, delegate { Shutdown(); });

            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = Icon ?? SystemIcons.Application;
            _trayIcon.Text = "VoiceDuck";
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += delegate { ShowMainWindow(); };
        }

        private void LoadSettingsIntoControls()
        {
            _updatingControls = true;
            try
            {
                _settings.Normalize();
                _duckTrack.Value = Clamp((int)Math.Round(_settings.DuckRatio * 100), _duckTrack.Minimum, _duckTrack.Maximum);
                _thresholdTrack.Value = Clamp((int)Math.Round(_settings.ThresholdDb), _thresholdTrack.Minimum, _thresholdTrack.Maximum);
                _delayTrack.Value = Clamp(_settings.TriggerDelayMs, _delayTrack.Minimum, _delayTrack.Maximum);
                _holdTrack.Value = Clamp(_settings.HoldMs, _holdTrack.Minimum, _holdTrack.Maximum);
                _attackTrack.Value = Clamp(_settings.AttackMs, _attackTrack.Minimum, _attackTrack.Maximum);
                _releaseTrack.Value = Clamp(_settings.ReleaseMs, _releaseTrack.Minimum, _releaseTrack.Maximum);
                _duckAllCheck.Checked = _settings.DuckAllOtherAudio;
                _targetList.Enabled = !_settings.DuckAllOtherAudio;
                _startupCheck.Checked = StartupManager.IsEnabled();
                _minimizeCheck.Checked = _settings.MinimizeToTray;
                _presetBox.SelectedIndex = GuessPreset();
                UpdateSliderLabels();
                UpdateToggleAppearance();
            }
            finally { _updatingControls = false; }
        }

        private void UiTimerTick(object sender, EventArgs e)
        {
            EngineStatus status = _service.GetStatus();
            MusicShareStatus shareStatus = _musicShareService.GetStatus();
            HearingProtectionStatus protectionStatus = _hearingProtectionService.GetStatus();
            _musicShareButton.Text = shareStatus.Running ? "音乐分享 · 开启" : "通话音乐分享";
            _musicShareButton.ForeColor = shareStatus.Running ? GreenColor : TextColor;
            if (!String.IsNullOrWhiteSpace(protectionStatus.LastError))
            {
                _hearingProtectionButton.Text = "音量保护 · 错误";
                _hearingProtectionButton.ForeColor = OrangeColor;
            }
            else if (protectionStatus.Enabled)
            {
                _hearingProtectionButton.Text = protectionStatus.Attenuating
                    ? "音量保护 · 压低中"
                    : "音量保护 · " + _settings.HearingProtectionMaxVolume.ToString("0%");
                _hearingProtectionButton.ForeColor = protectionStatus.Attenuating ? OrangeColor : GreenColor;
            }
            else
            {
                _hearingProtectionButton.Text = "音量保护";
                _hearingProtectionButton.ForeColor = TextColor;
            }
            if (!String.IsNullOrEmpty(status.LastError))
            {
                SetStatus("音频错误", OrangeColor, status.LastError);
            }
            else if (!status.Enabled)
            {
                SetStatus("已暂停", SubtleColor, "配置已保存；点击开始后监听通话应用");
            }
            else if (status.Ducking)
            {
                string app = FriendlyName(status.TriggerProcess);
                SetStatus("正在闪避", GreenColor, app + " 正在发声  ·  峰值 " + status.TriggerPeakDb.ToString("0") + " dB");
            }
            else
            {
                SetStatus("监听中", AccentColor, "已发现 " + status.SessionCount + " 个音频会话，等待对方讲话");
            }

            _refreshTicks++;
            if (_refreshTicks >= 8)
            {
                _refreshTicks = 0;
                RefreshApplicationLists(false);
            }
        }

        private void RefreshApplicationLists(bool force)
        {
            List<AudioSessionInfo> sessions = _service.GetSessions();
            var activeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AudioSessionInfo session in sessions)
            {
                string name = AppSettings.NormalizeProcessName(session.ProcessName);
                if (!session.IsSystemSounds && name.Length > 0 && name != "voiceduck") activeNames.Add(name);
            }
            string signature = String.Join("|", activeNames.OrderBy(delegate(string s) { return s; }).ToArray());
            if (!force && signature == _sessionSignature) return;
            _sessionSignature = signature;

            var triggerNames = new HashSet<string>(activeNames, StringComparer.OrdinalIgnoreCase);
            triggerNames.UnionWith(_settings.TriggerApps);
            triggerNames.UnionWith(new[] { "wechat", "weixin", "qq", "wxwork", "discord", "teams", "ms-teams", "zoom" });

            var targetNames = new HashSet<string>(activeNames, StringComparer.OrdinalIgnoreCase);
            targetNames.UnionWith(_settings.TargetApps);
            targetNames.UnionWith(new[] { "spotify", "qqmusic", "cloudmusic", "musicbee", "vlc", "potplayermini64", "chrome", "msedge" });

            _updatingControls = true;
            try
            {
                FillApplicationList(_triggerList, triggerNames, _settings.TriggerApps, activeNames);
                FillApplicationList(_targetList, targetNames, _settings.TargetApps, activeNames);
            }
            finally { _updatingControls = false; }
        }

        private static void FillApplicationList(
            CheckedListBox list,
            IEnumerable<string> names,
            IEnumerable<string> selectedNames,
            HashSet<string> activeNames)
        {
            var selected = new HashSet<string>(selectedNames, StringComparer.OrdinalIgnoreCase);
            var choices = new List<AppChoice>();
            foreach (string name in names)
            {
                string normalized = AppSettings.NormalizeProcessName(name);
                if (normalized.Length == 0 || normalized == "voiceduck") continue;
                choices.Add(new AppChoice
                {
                    ProcessName = normalized,
                    Text = FriendlyName(normalized) + "  ·  " + (activeNames.Contains(normalized) ? "正在运行" : "未运行")
                });
            }
            choices.Sort(delegate(AppChoice left, AppChoice right)
            {
                bool leftActive = activeNames.Contains(left.ProcessName);
                bool rightActive = activeNames.Contains(right.ProcessName);
                if (leftActive != rightActive) return leftActive ? -1 : 1;
                return StringComparer.CurrentCultureIgnoreCase.Compare(left.Text, right.Text);
            });

            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (AppChoice choice in choices)
                    list.Items.Add(choice, selected.Contains(choice.ProcessName));
            }
            finally { list.EndUpdate(); }
        }

        private void TriggerListItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_updatingControls) return;
            AppChoice choice = _triggerList.Items[e.Index] as AppChoice;
            if (choice == null) return;
            SetMembership(_settings.TriggerApps, choice.ProcessName, e.NewValue == CheckState.Checked);
            BeginInvoke(new Action(ApplySettings));
        }

        private void TargetListItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_updatingControls) return;
            AppChoice choice = _targetList.Items[e.Index] as AppChoice;
            if (choice == null) return;
            SetMembership(_settings.TargetApps, choice.ProcessName, e.NewValue == CheckState.Checked);
            BeginInvoke(new Action(ApplySettings));
        }

        private void SliderChanged(object sender, EventArgs e)
        {
            if (_updatingControls) return;
            _settings.DuckRatio = _duckTrack.Value / 100.0f;
            _settings.ThresholdDb = _thresholdTrack.Value;
            _settings.TriggerDelayMs = _delayTrack.Value;
            _settings.HoldMs = _holdTrack.Value;
            _settings.AttackMs = _attackTrack.Value;
            _settings.ReleaseMs = _releaseTrack.Value;
            UpdateSliderLabels();
            _updatingControls = true;
            _presetBox.SelectedIndex = GuessPreset();
            _updatingControls = false;
            ApplySettings();
        }

        private void PresetChanged(object sender, EventArgs e)
        {
            if (_updatingControls || _presetBox.SelectedIndex < 0 || _presetBox.SelectedIndex == 4) return;
            _updatingControls = true;
            try
            {
                if (_presetBox.SelectedIndex == 0) SetSliderValues(25, -42, 180, 1400, 160, 850);
                else if (_presetBox.SelectedIndex == 1) SetSliderValues(18, -40, 120, 1200, 90, 650);
                else if (_presetBox.SelectedIndex == 2) SetSliderValues(12, -46, 250, 2200, 120, 1000);
                else if (_presetBox.SelectedIndex == 3) SetSliderValues(40, -45, 220, 1800, 300, 1200);
            }
            finally { _updatingControls = false; }
            SliderChanged(sender, e);
        }

        private void SetSliderValues(int duck, int threshold, int delay, int hold, int attack, int release)
        {
            _duckTrack.Value = duck;
            _thresholdTrack.Value = threshold;
            _delayTrack.Value = delay;
            _holdTrack.Value = hold;
            _attackTrack.Value = attack;
            _releaseTrack.Value = release;
        }

        private int GuessPreset()
        {
            if (MatchesPreset(25, -42, 180, 1400, 160, 850)) return 0;
            if (MatchesPreset(18, -40, 120, 1200, 90, 650)) return 1;
            if (MatchesPreset(12, -46, 250, 2200, 120, 1000)) return 2;
            if (MatchesPreset(40, -45, 220, 1800, 300, 1200)) return 3;
            return 4;
        }

        private bool MatchesPreset(int duck, int threshold, int delay, int hold, int attack, int release)
        {
            return _duckTrack.Value == duck && _thresholdTrack.Value == threshold &&
                   _delayTrack.Value == delay && _holdTrack.Value == hold &&
                   _attackTrack.Value == attack && _releaseTrack.Value == release;
        }

        private void UpdateSliderLabels()
        {
            _duckValue.Text = _duckTrack.Value + "%";
            _thresholdValue.Text = _thresholdTrack.Value + " dB";
            _delayValue.Text = _delayTrack.Value + " ms";
            _holdValue.Text = (_holdTrack.Value / 1000.0).ToString("0.0") + " s";
            _attackValue.Text = _attackTrack.Value + " ms";
            _releaseValue.Text = (_releaseTrack.Value / 1000.0).ToString("0.0") + " s";
        }

        private void StartupCheckChanged(object sender, EventArgs e)
        {
            if (_updatingControls) return;
            try { StartupManager.SetEnabled(_startupCheck.Checked); }
            catch (Exception ex)
            {
                _updatingControls = true;
                _startupCheck.Checked = !_startupCheck.Checked;
                _updatingControls = false;
                MessageBox.Show(this, "无法修改开机启动：\n" + ex.Message, "VoiceDuck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ApplySettings()
        {
            _settings.Normalize();
            try { SettingsStore.Save(_settings); } catch { }
            _service.UpdateSettings(_settings);
            _hearingProtectionService.UpdateSettings(_settings);
        }

        private void UpdateToggleAppearance()
        {
            if (_settings.Enabled)
            {
                _toggleButton.Text = "暂停自动闪避";
                _toggleButton.BackColor = Color.FromArgb(48, 63, 82);
            }
            else
            {
                _toggleButton.Text = "开始自动闪避";
                _toggleButton.BackColor = AccentColor;
            }
        }

        private void SetStatus(string text, Color color, string detail)
        {
            _statusBadge.Text = text;
            _statusBadge.ForeColor = color;
            _statusDetail.Text = detail;
            _trayIcon.Text = ("VoiceDuck · " + text).Substring(0, Math.Min(63, ("VoiceDuck · " + text).Length));
        }

        private void ShowMainWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void ShowMusicShare()
        {
            ShowMainWindow();
            if (_musicShareForm == null || _musicShareForm.IsDisposed)
                _musicShareForm = new MusicShareForm(
                    _settings,
                    _musicShareService,
                    _microphoneSwitcher,
                    ApplySettings);
            if (!_musicShareForm.Visible) _musicShareForm.Show(this);
            _musicShareForm.Activate();
        }

        private void ShowHearingProtection()
        {
            ShowMainWindow();
            if (_hearingProtectionForm == null || _hearingProtectionForm.IsDisposed)
                _hearingProtectionForm = new HearingProtectionForm(
                    _settings,
                    _hearingProtectionService,
                    ApplySettings);
            if (!_hearingProtectionForm.Visible) _hearingProtectionForm.Show(this);
            _hearingProtectionForm.Activate();
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_allowExit && _settings.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                _trayIcon.ShowBalloonTip(1500, "VoiceDuck 仍在运行", "双击托盘图标可以重新打开。", ToolTipIcon.Info);
            }
        }

        private static Panel CreateCard(Rectangle bounds, Color color)
        {
            var panel = new Panel();
            panel.Bounds = bounds;
            panel.BackColor = color;
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

        private static CheckBox CreateCheckBox(string text)
        {
            var check = new CheckBox();
            check.Text = text;
            check.ForeColor = TextColor;
            check.BackColor = Color.Transparent;
            check.AutoSize = false;
            return check;
        }

        private static CheckedListBox CreateCheckedList()
        {
            var list = new CheckedListBox();
            list.CheckOnClick = true;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Color.FromArgb(23, 30, 42);
            list.ForeColor = TextColor;
            list.IntegralHeight = false;
            list.ItemHeight = 23;
            return list;
        }

        private static void AddSectionHeader(Panel panel, string title, string subtitle, string tag)
        {
            Label titleLabel = CreateLabel(title, 12.0f, FontStyle.Bold, TextColor);
            titleLabel.SetBounds(16, 12, 180, 26);
            panel.Controls.Add(titleLabel);
            Label subtitleLabel = CreateLabel(subtitle, 8.8f, FontStyle.Regular, SubtleColor);
            subtitleLabel.SetBounds(18, 39, 270, 22);
            panel.Controls.Add(subtitleLabel);
            Label tagLabel = CreateLabel(tag, 8.5f, FontStyle.Bold, AccentColor);
            tagLabel.TextAlign = ContentAlignment.MiddleCenter;
            tagLabel.BackColor = Color.FromArgb(39, 52, 74);
            tagLabel.SetBounds(panel.Width - 91, 18, 72, 28);
            panel.Controls.Add(tagLabel);
        }

        private static TrackBar AddSlider(
            Panel parent,
            int x,
            int y,
            string title,
            int minimum,
            int maximum,
            int value,
            out Label valueLabel)
        {
            Label label = CreateLabel(title, 9.0f, FontStyle.Regular, SubtleColor);
            label.SetBounds(x, y, 165, 22);
            parent.Controls.Add(label);
            valueLabel = CreateLabel(String.Empty, 9.0f, FontStyle.Bold, TextColor);
            valueLabel.TextAlign = ContentAlignment.TopRight;
            valueLabel.SetBounds(x + 170, y, 78, 22);
            parent.Controls.Add(valueLabel);

            TrackBar track = new TrackBar();
            track.Minimum = minimum;
            track.Maximum = maximum;
            track.Value = Clamp(value, minimum, maximum);
            track.TickStyle = TickStyle.None;
            track.BackColor = CardColor;
            track.SetBounds(x - 4, y + 24, 260, 40);
            parent.Controls.Add(track);
            return track;
        }

        private static void SetMembership(List<string> list, string name, bool shouldContain)
        {
            string normalized = AppSettings.NormalizeProcessName(name);
            list.RemoveAll(delegate(string item)
            {
                return String.Equals(AppSettings.NormalizeProcessName(item), normalized, StringComparison.OrdinalIgnoreCase);
            });
            if (shouldContain) list.Add(normalized);
        }

        private static string FriendlyName(string processName)
        {
            string name = AppSettings.NormalizeProcessName(processName);
            if (name == "wechat" || name == "weixin") return "微信 · " + name + ".exe";
            if (name == "wxwork") return "企业微信 · WXWork.exe";
            if (name == "qq") return "QQ · QQ.exe";
            if (name == "qqmusic") return "QQ 音乐 · QQMusic.exe";
            if (name == "cloudmusic") return "网易云音乐 · cloudmusic.exe";
            if (name == "spotify") return "Spotify · Spotify.exe";
            if (name == "musicbee") return "MusicBee · MusicBee.exe";
            if (name == "vlc") return "VLC · vlc.exe";
            if (name == "potplayermini64") return "PotPlayer · PotPlayerMini64.exe";
            if (name == "discord") return "Discord · Discord.exe";
            if (name == "teams" || name == "ms-teams") return "Microsoft Teams · " + name + ".exe";
            if (name == "zoom") return "Zoom · Zoom.exe";
            if (name == "chrome") return "Google Chrome · chrome.exe";
            if (name == "msedge") return "Microsoft Edge · msedge.exe";
            return name.Length == 0 ? "未知应用" : name + ".exe";
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
