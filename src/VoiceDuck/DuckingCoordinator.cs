using System;
using System.Collections.Generic;

namespace VoiceDuck
{
    internal interface IDuckableSession
    {
        string Id { get; }
        string ProcessName { get; }
        bool IsSystemSounds { get; }
        float ReadPeak();
        float ReadVolume();
        void WriteVolume(float volume);
    }

    internal sealed class DuckingCoordinator
    {
        private sealed class VolumeState
        {
            public float Baseline;
            public float LastWritten;
            public bool HasWritten;
        }

        private readonly VoiceGate _voiceGate = new VoiceGate();
        private readonly Dictionary<string, VolumeState> _volumes =
            new Dictionary<string, VolumeState>(StringComparer.OrdinalIgnoreCase);
        private bool _wasEnabled;

        public bool IsDucking { get; private set; }
        public string TriggerProcess { get; private set; }
        public float TriggerPeakDb { get; private set; }

        public void Tick(IList<IDuckableSession> sessions, AppSettings settings, int elapsedMs)
        {
            if (sessions == null) throw new ArgumentNullException("sessions");
            if (settings == null) throw new ArgumentNullException("settings");

            if (!settings.Enabled)
            {
                if (_wasEnabled || _volumes.Count > 0) RestoreAll(sessions, true, settings.ReleaseMs, elapsedMs);
                _voiceGate.Reset();
                IsDucking = false;
                TriggerProcess = String.Empty;
                TriggerPeakDb = -96.0f;
                _wasEnabled = false;
                return;
            }

            _wasEnabled = true;
            var triggerNames = new HashSet<string>(settings.TriggerApps, StringComparer.OrdinalIgnoreCase);
            float peak = 0.0f;
            string trigger = String.Empty;

            foreach (IDuckableSession session in sessions)
            {
                string processName = AppSettings.NormalizeProcessName(session.ProcessName);
                if (!triggerNames.Contains(processName)) continue;
                float candidate = SafePeak(session);
                if (candidate > peak)
                {
                    peak = candidate;
                    trigger = processName;
                }
            }

            bool shouldDuck = _voiceGate.Update(
                peak,
                settings.ThresholdDb,
                settings.TriggerDelayMs,
                settings.HoldMs,
                elapsedMs);

            IsDucking = shouldDuck;
            TriggerProcess = trigger;
            TriggerPeakDb = VoiceGate.LinearToDb(peak);

            if (shouldDuck)
                DuckTargets(sessions, settings, triggerNames, elapsedMs);
            else
                RestoreAll(sessions, false, settings.ReleaseMs, elapsedMs);

            RemoveMissingSessions(sessions);
        }

        public void RestoreNow(IList<IDuckableSession> sessions)
        {
            foreach (IDuckableSession session in sessions)
            {
                VolumeState state;
                if (_volumes.TryGetValue(session.Id, out state))
                    SafeWrite(session, state.Baseline);
            }
            _volumes.Clear();
            IsDucking = false;
            _voiceGate.Reset();
        }

        private void DuckTargets(
            IList<IDuckableSession> sessions,
            AppSettings settings,
            HashSet<string> triggerNames,
            int elapsedMs)
        {
            var targetNames = new HashSet<string>(settings.TargetApps, StringComparer.OrdinalIgnoreCase);
            foreach (IDuckableSession session in sessions)
            {
                string processName = AppSettings.NormalizeProcessName(session.ProcessName);
                if (session.IsSystemSounds || processName == "voiceduck" || triggerNames.Contains(processName))
                    continue;
                if (!settings.DuckAllOtherAudio && !targetNames.Contains(processName))
                    continue;

                float current = SafeVolume(session);
                VolumeState state;
                if (!_volumes.TryGetValue(session.Id, out state))
                {
                    state = new VolumeState { Baseline = current, LastWritten = current, HasWritten = false };
                    _volumes[session.Id] = state;
                }
                else if (state.HasWritten && Math.Abs(current - state.LastWritten) > 0.035f)
                {
                    // Respect a manual Volume Mixer change made while ducking.
                    state.Baseline = Clamp(current / Math.Max(settings.DuckRatio, 0.02f));
                }

                float target = state.Baseline * settings.DuckRatio;
                float next = Smooth(current, target, settings.AttackMs, elapsedMs);
                SafeWrite(session, next);
                state.LastWritten = next;
                state.HasWritten = true;
            }
        }

        private void RestoreAll(
            IList<IDuckableSession> sessions,
            bool immediate,
            int releaseMs,
            int elapsedMs)
        {
            var completed = new List<string>();
            foreach (IDuckableSession session in sessions)
            {
                VolumeState state;
                if (!_volumes.TryGetValue(session.Id, out state)) continue;
                float current = SafeVolume(session);
                float next = immediate ? state.Baseline : Smooth(current, state.Baseline, releaseMs, elapsedMs);
                SafeWrite(session, next);
                state.LastWritten = next;
                state.HasWritten = true;
                if (immediate || Math.Abs(next - state.Baseline) < 0.003f)
                {
                    SafeWrite(session, state.Baseline);
                    completed.Add(session.Id);
                }
            }
            foreach (string id in completed) _volumes.Remove(id);
        }

        private void RemoveMissingSessions(IList<IDuckableSession> sessions)
        {
            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IDuckableSession session in sessions) live.Add(session.Id);
            var stale = new List<string>();
            foreach (string id in _volumes.Keys)
                if (!live.Contains(id)) stale.Add(id);
            foreach (string id in stale) _volumes.Remove(id);
        }

        private static float Smooth(float current, float target, int durationMs, int elapsedMs)
        {
            if (durationMs <= 0) return target;
            float alpha = Math.Min(1.0f, (float)elapsedMs / durationMs);
            return Clamp(current + (target - current) * alpha);
        }

        private static float SafePeak(IDuckableSession session)
        {
            try { return Clamp(session.ReadPeak()); }
            catch { return 0.0f; }
        }

        private static float SafeVolume(IDuckableSession session)
        {
            try { return Clamp(session.ReadVolume()); }
            catch { return 1.0f; }
        }

        private static void SafeWrite(IDuckableSession session, float value)
        {
            try { session.WriteVolume(Clamp(value)); }
            catch { }
        }

        private static float Clamp(float value)
        {
            return Math.Max(0.0f, Math.Min(1.0f, value));
        }
    }
}
