using System;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;

namespace VoiceDuck
{
    internal static class HearingProtectionCore
    {
        public static float PeakToDb(float peak)
        {
            if (peak <= 0.000001f) return -96.0f;
            return (float)(20.0 * Math.Log10(peak));
        }

        public static float CalculateTargetVolumeDb(
            float baseVolumeDb,
            float inputPeak,
            float outputPeakLimitDb,
            float minimumVolumeDb)
        {
            float peakDb = PeakToDb(inputPeak);
            float peakSafeVolumeDb = outputPeakLimitDb - peakDb;
            return Math.Max(minimumVolumeDb, Math.Min(baseVolumeDb, peakSafeVolumeDb));
        }

        public static float RecoverToward(
            float currentVolumeDb,
            float targetVolumeDb,
            int elapsedMilliseconds,
            int recoveryMilliseconds)
        {
            if (targetVolumeDb <= currentVolumeDb) return targetVolumeDb;
            int safeRecovery = Math.Max(500, recoveryMilliseconds);
            float step = Math.Max(0.15f, 36.0f * Math.Max(1, elapsedMilliseconds) / safeRecovery);
            return Math.Min(targetVolumeDb, currentVolumeDb + step);
        }
    }

    internal sealed class HearingProtectionStatus
    {
        public bool Enabled { get; set; }
        public bool Attenuating { get; set; }
        public string DeviceName { get; set; }
        public float CurrentVolumePercent { get; set; }
        public float InputPeakDb { get; set; }
        public float EstimatedOutputPeakDb { get; set; }
        public float AttenuationDb { get; set; }
        public string LastError { get; set; }

        public HearingProtectionStatus Clone()
        {
            return (HearingProtectionStatus)MemberwiseClone();
        }
    }

    internal sealed class HearingProtectionService : IDisposable
    {
        private const int PollMilliseconds = 25;
        private readonly object _sync = new object();
        private AppSettings _settings;
        private HearingProtectionStatus _status = new HearingProtectionStatus
        {
            DeviceName = "正在检测默认输出设备…",
            InputPeakDb = -96.0f,
            EstimatedOutputPeakDb = -96.0f,
            LastError = String.Empty
        };
        private Thread _thread;
        private volatile bool _running;

        public HearingProtectionService(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            _settings = settings.Clone();
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(WorkerLoop);
            _thread.Name = "VoiceDuck Hearing Protection";
            _thread.IsBackground = true;
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        public void UpdateSettings(AppSettings settings)
        {
            if (settings == null) return;
            lock (_sync) _settings = settings.Clone();
        }

        public HearingProtectionStatus GetStatus()
        {
            lock (_sync) return _status.Clone();
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            Thread thread = _thread;
            if (thread != null && thread != Thread.CurrentThread) thread.Join(3500);
            _thread = null;
        }

        public void Dispose()
        {
            Stop();
        }

        private void WorkerLoop()
        {
            int comResult = CoInitializeEx(IntPtr.Zero, 0);
            bool shouldUninitialize = comResult >= 0;
            MMDeviceEnumerator enumerator = null;
            MMDevice device = null;
            string deviceId = String.Empty;
            float baseVolumeDb = 0.0f;
            float baseVolumeScalar = 0.0f;
            float lastAppliedVolumeDb = Single.NaN;
            bool attenuating = false;
            bool wasEnabled = false;
            int refreshElapsed = 1000;

            try
            {
                enumerator = new MMDeviceEnumerator();
                while (_running)
                {
                    AppSettings settings;
                    lock (_sync) settings = _settings.Clone();
                    try
                    {
                        if (device == null || refreshElapsed >= 1000)
                        {
                            MMDevice currentDefault = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                            if (device == null || !String.Equals(currentDefault.ID, deviceId, StringComparison.OrdinalIgnoreCase))
                            {
                                RestoreTemporaryAttenuation(device, attenuating, baseVolumeDb);
                                if (device != null) device.Dispose();
                                device = currentDefault;
                                deviceId = device.ID;
                                baseVolumeDb = device.AudioEndpointVolume.MasterVolumeLevel;
                                baseVolumeScalar = device.AudioEndpointVolume.MasterVolumeLevelScalar;
                                lastAppliedVolumeDb = Single.NaN;
                                attenuating = false;
                                wasEnabled = false;
                            }
                            else
                            {
                                currentDefault.Dispose();
                            }
                            refreshElapsed = 0;
                        }

                        AudioEndpointVolume endpointVolume = device.AudioEndpointVolume;
                        float currentVolumeDb = endpointVolume.MasterVolumeLevel;
                        float currentVolumeScalar = endpointVolume.MasterVolumeLevelScalar;
                        float inputPeak = device.AudioMeterInformation.MasterPeakValue;
                        float inputPeakDb = HearingProtectionCore.PeakToDb(inputPeak);

                        if (!settings.HearingProtectionEnabled)
                        {
                            if (wasEnabled && attenuating)
                            {
                                endpointVolume.MasterVolumeLevel = baseVolumeDb;
                                currentVolumeDb = endpointVolume.MasterVolumeLevel;
                                currentVolumeScalar = endpointVolume.MasterVolumeLevelScalar;
                            }
                            wasEnabled = false;
                            attenuating = false;
                            lastAppliedVolumeDb = Single.NaN;
                            baseVolumeDb = currentVolumeDb;
                            baseVolumeScalar = currentVolumeScalar;
                            PublishStatus(settings, device, currentVolumeDb, currentVolumeScalar,
                                inputPeakDb, baseVolumeDb, false, String.Empty);
                            Thread.Sleep(50);
                            refreshElapsed += 50;
                            continue;
                        }

                        if (!wasEnabled)
                        {
                            baseVolumeDb = currentVolumeDb;
                            baseVolumeScalar = currentVolumeScalar;
                            attenuating = false;
                            lastAppliedVolumeDb = Single.NaN;
                        }
                        wasEnabled = true;

                        float toleranceDb = Math.Max(0.6f, endpointVolume.VolumeRange.IncrementDecibels * 2.0f);
                        bool externalVolumeChange = attenuating && !Single.IsNaN(lastAppliedVolumeDb) &&
                            Math.Abs(currentVolumeDb - lastAppliedVolumeDb) > toleranceDb;
                        if (externalVolumeChange)
                        {
                            if (currentVolumeScalar > settings.HearingProtectionMaxVolume)
                            {
                                endpointVolume.MasterVolumeLevelScalar = settings.HearingProtectionMaxVolume;
                                currentVolumeDb = endpointVolume.MasterVolumeLevel;
                                currentVolumeScalar = endpointVolume.MasterVolumeLevelScalar;
                            }
                            baseVolumeDb = currentVolumeDb;
                            baseVolumeScalar = currentVolumeScalar;
                            attenuating = false;
                            lastAppliedVolumeDb = Single.NaN;
                        }

                        if (!attenuating)
                        {
                            if (currentVolumeScalar > settings.HearingProtectionMaxVolume + 0.002f)
                            {
                                endpointVolume.MasterVolumeLevelScalar = settings.HearingProtectionMaxVolume;
                                currentVolumeDb = endpointVolume.MasterVolumeLevel;
                                currentVolumeScalar = endpointVolume.MasterVolumeLevelScalar;
                            }
                            baseVolumeDb = currentVolumeDb;
                            baseVolumeScalar = currentVolumeScalar;
                        }
                        else if (baseVolumeScalar > settings.HearingProtectionMaxVolume + 0.002f)
                        {
                            baseVolumeScalar = settings.HearingProtectionMaxVolume;
                            baseVolumeDb = Math.Min(baseVolumeDb, currentVolumeDb);
                        }

                        float targetVolumeDb = HearingProtectionCore.CalculateTargetVolumeDb(
                            baseVolumeDb,
                            inputPeak,
                            settings.HearingProtectionPeakLimitDb,
                            endpointVolume.VolumeRange.MinDecibels);
                        if (targetVolumeDb < currentVolumeDb - 0.05f)
                        {
                            endpointVolume.MasterVolumeLevel = targetVolumeDb;
                            currentVolumeDb = endpointVolume.MasterVolumeLevel;
                            currentVolumeScalar = endpointVolume.MasterVolumeLevelScalar;
                            lastAppliedVolumeDb = currentVolumeDb;
                        }
                        else if (currentVolumeDb < targetVolumeDb - 0.05f)
                        {
                            float recovered = HearingProtectionCore.RecoverToward(
                                currentVolumeDb,
                                targetVolumeDb,
                                PollMilliseconds,
                                settings.HearingProtectionRecoveryMs);
                            endpointVolume.MasterVolumeLevel = recovered;
                            currentVolumeDb = endpointVolume.MasterVolumeLevel;
                            currentVolumeScalar = endpointVolume.MasterVolumeLevelScalar;
                            lastAppliedVolumeDb = currentVolumeDb;
                        }

                        attenuating = currentVolumeDb < baseVolumeDb - 0.10f;
                        if (!attenuating) lastAppliedVolumeDb = Single.NaN;
                        PublishStatus(settings, device, currentVolumeDb, currentVolumeScalar,
                            inputPeakDb, baseVolumeDb, attenuating, String.Empty);
                    }
                    catch (Exception exception)
                    {
                        PublishError(settings, exception.Message);
                        RestoreTemporaryAttenuation(device, attenuating, baseVolumeDb);
                        try { if (device != null) device.Dispose(); } catch { }
                        device = null;
                        deviceId = String.Empty;
                        attenuating = false;
                        wasEnabled = false;
                        lastAppliedVolumeDb = Single.NaN;
                        refreshElapsed = 1000;
                        Thread.Sleep(100);
                    }

                    Thread.Sleep(PollMilliseconds);
                    refreshElapsed += PollMilliseconds;
                }
            }
            finally
            {
                RestoreTemporaryAttenuation(device, attenuating, baseVolumeDb);
                if (device != null) device.Dispose();
                if (enumerator != null) enumerator.Dispose();
                if (shouldUninitialize) CoUninitialize();
            }
        }

        private void PublishStatus(
            AppSettings settings,
            MMDevice device,
            float currentVolumeDb,
            float currentVolumeScalar,
            float inputPeakDb,
            float baseVolumeDb,
            bool attenuating,
            string error)
        {
            lock (_sync)
            {
                _status = new HearingProtectionStatus
                {
                    Enabled = settings.HearingProtectionEnabled,
                    Attenuating = attenuating,
                    DeviceName = device == null ? "未找到默认输出设备" : device.FriendlyName,
                    CurrentVolumePercent = currentVolumeScalar * 100.0f,
                    InputPeakDb = inputPeakDb,
                    EstimatedOutputPeakDb = inputPeakDb + currentVolumeDb,
                    AttenuationDb = Math.Max(0.0f, baseVolumeDb - currentVolumeDb),
                    LastError = error ?? String.Empty
                };
            }
        }

        private void PublishError(AppSettings settings, string error)
        {
            lock (_sync)
            {
                _status.Enabled = settings.HearingProtectionEnabled;
                _status.Attenuating = false;
                _status.LastError = error ?? "未知音量保护错误";
            }
        }

        private static void RestoreTemporaryAttenuation(
            MMDevice device,
            bool attenuating,
            float baseVolumeDb)
        {
            if (device == null || !attenuating) return;
            try { device.AudioEndpointVolume.MasterVolumeLevel = baseVolumeDb; }
            catch { }
        }

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();
    }
}
