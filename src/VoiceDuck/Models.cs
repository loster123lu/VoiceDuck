using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace VoiceDuck
{
    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember] public int SettingsVersion { get; set; }
        [DataMember] public bool Enabled { get; set; }
        [DataMember] public bool DuckAllOtherAudio { get; set; }
        [DataMember] public bool MinimizeToTray { get; set; }
        [DataMember] public float DuckRatio { get; set; }
        [DataMember] public float ThresholdDb { get; set; }
        [DataMember] public int TriggerDelayMs { get; set; }
        [DataMember] public int HoldMs { get; set; }
        [DataMember] public int AttackMs { get; set; }
        [DataMember] public int ReleaseMs { get; set; }
        [DataMember] public string ShareMicrophoneDevice { get; set; }
        [DataMember] public string ShareMonitorDevice { get; set; }
        [DataMember] public float ShareMicrophoneGain { get; set; }
        [DataMember] public float ShareMusicGain { get; set; }
        [DataMember] public bool ShareAutoSwitchMicrophone { get; set; }
        [DataMember] public List<string> TriggerApps { get; set; }
        [DataMember] public List<string> TargetApps { get; set; }

        public static AppSettings CreateDefault()
        {
            return new AppSettings
            {
                SettingsVersion = 4,
                Enabled = false,
                DuckAllOtherAudio = false,
                MinimizeToTray = true,
                DuckRatio = 0.25f,
                ThresholdDb = -42.0f,
                TriggerDelayMs = 180,
                HoldMs = 1400,
                AttackMs = 160,
                ReleaseMs = 850,
                ShareMicrophoneDevice = String.Empty,
                ShareMonitorDevice = String.Empty,
                ShareMicrophoneGain = 0.65f,
                ShareMusicGain = 0.55f,
                ShareAutoSwitchMicrophone = true,
                TriggerApps = new List<string>
                {
                    "wechat", "weixin", "qq", "wxwork", "discord", "teams", "ms-teams", "zoom"
                },
                TargetApps = new List<string>
                {
                    "spotify", "qqmusic", "cloudmusic", "musicbee", "vlc", "potplayermini64"
                }
            };
        }

        public void Normalize()
        {
            if (SettingsVersion < 2)
            {
                ShareMicrophoneGain = 0.65f;
                ShareMusicGain = 0.55f;
            }
            if (SettingsVersion < 4) ShareAutoSwitchMicrophone = true;
            SettingsVersion = 4;
            DuckRatio = Clamp(DuckRatio, 0.02f, 1.0f);
            ThresholdDb = Clamp(ThresholdDb, -70.0f, -5.0f);
            TriggerDelayMs = Clamp(TriggerDelayMs, 0, 2000);
            HoldMs = Clamp(HoldMs, 0, 8000);
            AttackMs = Clamp(AttackMs, 0, 4000);
            ReleaseMs = Clamp(ReleaseMs, 0, 6000);
            ShareMicrophoneDevice = (ShareMicrophoneDevice ?? String.Empty).Trim();
            ShareMonitorDevice = (ShareMonitorDevice ?? String.Empty).Trim();
            ShareMicrophoneGain = Clamp(ShareMicrophoneGain, 0.0f, 1.5f);
            ShareMusicGain = Clamp(ShareMusicGain, 0.0f, 1.5f);
            if (TriggerApps == null) TriggerApps = new List<string>();
            if (TargetApps == null) TargetApps = new List<string>();
            TriggerApps = NormalizeList(TriggerApps);
            TargetApps = NormalizeList(TargetApps);
        }

        public AppSettings Clone()
        {
            return new AppSettings
            {
                SettingsVersion = SettingsVersion,
                Enabled = Enabled,
                DuckAllOtherAudio = DuckAllOtherAudio,
                MinimizeToTray = MinimizeToTray,
                DuckRatio = DuckRatio,
                ThresholdDb = ThresholdDb,
                TriggerDelayMs = TriggerDelayMs,
                HoldMs = HoldMs,
                AttackMs = AttackMs,
                ReleaseMs = ReleaseMs,
                ShareMicrophoneDevice = ShareMicrophoneDevice,
                ShareMonitorDevice = ShareMonitorDevice,
                ShareMicrophoneGain = ShareMicrophoneGain,
                ShareMusicGain = ShareMusicGain,
                ShareAutoSwitchMicrophone = ShareAutoSwitchMicrophone,
                TriggerApps = new List<string>(TriggerApps ?? new List<string>()),
                TargetApps = new List<string>(TargetApps ?? new List<string>())
            };
        }

        public static string NormalizeProcessName(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return String.Empty;
            string result = value.Trim().ToLowerInvariant();
            if (result.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                result = result.Substring(0, result.Length - 4);
            return result;
        }

        private static List<string> NormalizeList(IEnumerable<string> values)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in values)
            {
                string normalized = NormalizeProcessName(value);
                if (normalized.Length > 0 && seen.Add(normalized)) result.Add(normalized);
            }
            return result;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }

    internal sealed class AudioSessionInfo
    {
        public string Id { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
        public string DisplayName { get; set; }
        public string DeviceName { get; set; }
        public float Peak { get; set; }
        public float Volume { get; set; }
        public bool IsSystemSounds { get; set; }

        public float PeakDb
        {
            get { return Peak <= 0.000001f ? -96.0f : (float)(20.0 * Math.Log10(Peak)); }
        }
    }

    internal sealed class EngineStatus
    {
        public bool Enabled { get; set; }
        public bool Ducking { get; set; }
        public string TriggerProcess { get; set; }
        public float TriggerPeakDb { get; set; }
        public int SessionCount { get; set; }
        public string LastError { get; set; }

        public EngineStatus Clone()
        {
            return (EngineStatus)MemberwiseClone();
        }
    }

    internal sealed class CallAudioActivity
    {
        public string ProcessName { get; set; }
        public float PeakDb { get; set; }

        public CallAudioActivity Clone()
        {
            return (CallAudioActivity)MemberwiseClone();
        }
    }
}
