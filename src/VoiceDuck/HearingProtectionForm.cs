using System;
using System.Drawing;
using System.Windows.Forms;

namespace VoiceDuck
{
    internal sealed class HearingProtectionForm : Form
    {
        private static readonly Color WindowColor = Color.FromArgb(17, 22, 31);
        private static readonly Color CardColor = Color.FromArgb(27, 35, 49);
        private static readonly Color TextColor = Color.FromArgb(239, 243, 250);
        private static readonly Color SubtleColor = Color.FromArgb(139, 151, 171);
        private static readonly Color AccentColor = Color.FromArgb(91, 140, 255);
        private static readonly Color GreenColor = Color.FromArgb(62, 207, 142);
        private static readonly Color OrangeColor = Color.FromArgb(255, 176, 84);

        private readonly AppSettings _settings;
        private readonly HearingProtectionService _service;
        private readonly Action _settingsChanged;
        private readonly Timer _timer;
        private CheckBox _enabledCheck;
        private TrackBar _maxVolumeTrack;
        private TrackBar _peakLimitTrack;
        private TrackBar _recoveryTrack;
        private Label _maxVolumeValue;
        private Label _peakLimitValue;
        private Label _recoveryValue;
        private Label _statusLabel;
        private Label _detailLabel;
        private bool _updating;
        private bool _ownerShuttingDown;

        public HearingProtectionForm(
            AppSettings settings,
            HearingProtectionService service,
            Action settingsChanged)
        {
            _settings = settings;
            _service = service;
            _settingsChanged = settingsChanged;

            Text = "VoiceDuck · 耳机音量保护";
            ClientSize = new Size(560, 430);
            MinimumSize = new Size(576, 469);
            MaximumSize = MinimumSize;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = WindowColor;
            ForeColor = TextColor;
            Font = new Font("Segoe UI", 9.2f, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            try
            {
                using (Icon executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    if (executableIcon != null) Icon = (Icon)executableIcon.Clone();
            }
            catch { }

            BuildInterface();
            LoadSettings();

            _timer = new Timer();
            _timer.Interval = 100;
            _timer.Tick += TimerTick;
            _timer.Start();
            FormClosing += HearingProtectionFormClosing;
        }

        public void PrepareForOwnerShutdown()
        {
            _ownerShuttingDown = true;
            _timer.Stop();
        }

        private void BuildInterface()
        {
            Label title = CreateLabel("耳机音量保护", 18.0f, FontStyle.Bold, TextColor);
            title.SetBounds(24, 18, 300, 34);
            Controls.Add(title);
            Label subtitle = CreateLabel(
                "使用 Windows 原有输出设备控制，不更换驱动、不改变播放路由",
                9.2f,
                FontStyle.Regular,
                SubtleColor);
            subtitle.SetBounds(26, 55, 510, 24);
            Controls.Add(subtitle);

            Panel card = new Panel();
            card.SetBounds(24, 90, 512, 250);
            card.BackColor = CardColor;
            Controls.Add(card);

            _enabledCheck = new CheckBox();
            _enabledCheck.Text = "启用音量保护";
            _enabledCheck.Font = new Font("Segoe UI", 10.0f, FontStyle.Bold, GraphicsUnit.Point);
            _enabledCheck.ForeColor = TextColor;
            _enabledCheck.BackColor = Color.Transparent;
            _enabledCheck.SetBounds(18, 13, 200, 28);
            _enabledCheck.CheckedChanged += SettingsChanged;
            card.Controls.Add(_enabledCheck);

            _maxVolumeTrack = AddSlider(card, 18, 50, "最大系统音量", 20, 100, out _maxVolumeValue);
            _peakLimitTrack = AddSlider(card, 18, 118, "突发峰值上限", -18, -1, out _peakLimitValue);
            _recoveryTrack = AddSlider(card, 18, 186, "峰值后恢复速度", 500, 8000, out _recoveryValue);
            _maxVolumeTrack.ValueChanged += SettingsChanged;
            _peakLimitTrack.ValueChanged += SettingsChanged;
            _recoveryTrack.ValueChanged += SettingsChanged;

            _statusLabel = CreateLabel("正在读取保护状态…", 10.0f, FontStyle.Bold, SubtleColor);
            _statusLabel.SetBounds(26, 352, 508, 25);
            Controls.Add(_statusLabel);
            _detailLabel = CreateLabel(String.Empty, 8.7f, FontStyle.Regular, SubtleColor);
            _detailLabel.SetBounds(26, 378, 508, 44);
            Controls.Add(_detailLabel);
        }

        private void LoadSettings()
        {
            _updating = true;
            try
            {
                _settings.Normalize();
                _enabledCheck.Checked = _settings.HearingProtectionEnabled;
                _maxVolumeTrack.Value = Clamp(
                    (int)Math.Round(_settings.HearingProtectionMaxVolume * 100.0f),
                    _maxVolumeTrack.Minimum,
                    _maxVolumeTrack.Maximum);
                _peakLimitTrack.Value = Clamp(
                    (int)Math.Round(_settings.HearingProtectionPeakLimitDb),
                    _peakLimitTrack.Minimum,
                    _peakLimitTrack.Maximum);
                _recoveryTrack.Value = Clamp(
                    _settings.HearingProtectionRecoveryMs,
                    _recoveryTrack.Minimum,
                    _recoveryTrack.Maximum);
                UpdateValueLabels();
            }
            finally { _updating = false; }
        }

        private void SettingsChanged(object sender, EventArgs eventArgs)
        {
            UpdateValueLabels();
            if (_updating) return;
            _settings.HearingProtectionEnabled = _enabledCheck.Checked;
            _settings.HearingProtectionMaxVolume = _maxVolumeTrack.Value / 100.0f;
            _settings.HearingProtectionPeakLimitDb = _peakLimitTrack.Value;
            _settings.HearingProtectionRecoveryMs = _recoveryTrack.Value;
            _settings.Normalize();
            if (_settingsChanged != null) _settingsChanged();
        }

        private void TimerTick(object sender, EventArgs eventArgs)
        {
            HearingProtectionStatus status = _service.GetStatus();
            if (!String.IsNullOrWhiteSpace(status.LastError))
            {
                _statusLabel.Text = "音量保护检测失败";
                _statusLabel.ForeColor = OrangeColor;
                _detailLabel.Text = status.LastError;
                return;
            }
            if (!status.Enabled)
            {
                _statusLabel.Text = "音量保护已关闭";
                _statusLabel.ForeColor = SubtleColor;
            }
            else if (status.Attenuating)
            {
                _statusLabel.Text = "正在压低突发声音 · 衰减 " + status.AttenuationDb.ToString("0.0") + " dB";
                _statusLabel.ForeColor = OrangeColor;
            }
            else
            {
                _statusLabel.Text = "保护中 · 等待突发峰值";
                _statusLabel.ForeColor = GreenColor;
            }
            _detailLabel.Text =
                status.DeviceName + "\r\n" +
                "当前系统音量 " + status.CurrentVolumePercent.ToString("0") + "% · " +
                "输入峰值 " + status.InputPeakDb.ToString("0.0") + " dB · " +
                "估算耳机输出 " + status.EstimatedOutputPeakDb.ToString("0.0") + " dB";
        }

        private void UpdateValueLabels()
        {
            _maxVolumeValue.Text = _maxVolumeTrack.Value + "%";
            _peakLimitValue.Text = _peakLimitTrack.Value + " dB";
            _recoveryValue.Text = (_recoveryTrack.Value / 1000.0).ToString("0.0") + " s";
        }

        private void HearingProtectionFormClosing(object sender, FormClosingEventArgs eventArgs)
        {
            if (!_ownerShuttingDown && eventArgs.CloseReason == CloseReason.UserClosing)
            {
                eventArgs.Cancel = true;
                Hide();
            }
        }

        private static TrackBar AddSlider(
            Control parent,
            int x,
            int y,
            string text,
            int minimum,
            int maximum,
            out Label valueLabel)
        {
            Label label = CreateLabel(text, 8.8f, FontStyle.Regular, SubtleColor);
            label.SetBounds(x, y, 180, 22);
            parent.Controls.Add(label);
            valueLabel = CreateLabel(String.Empty, 8.8f, FontStyle.Bold, TextColor);
            valueLabel.TextAlign = ContentAlignment.TopRight;
            valueLabel.SetBounds(410, y, 78, 22);
            parent.Controls.Add(valueLabel);
            var track = new TrackBar();
            track.Minimum = minimum;
            track.Maximum = maximum;
            track.TickStyle = TickStyle.None;
            track.BackColor = CardColor;
            track.SetBounds(x - 4, y + 22, 474, 34);
            parent.Controls.Add(track);
            return track;
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

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
