using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;

namespace VoiceDuck
{
    internal sealed class AudioSessionHandle : IDuckableSession, IDisposable
    {
        private AudioSessionControl _control;
        private SimpleAudioVolume _volume;
        private AudioMeterInformation _meter;

        public string Id { get; private set; }
        public int ProcessId { get; private set; }
        public string ProcessName { get; private set; }
        public string DisplayName { get; private set; }
        public string DeviceId { get; private set; }
        public bool IsSystemSounds { get; private set; }

        public AudioSessionHandle(AudioSessionControl control, string deviceId, int fallbackIndex)
        {
            if (control == null) throw new ArgumentNullException("control");
            _control = control;
            _volume = control.SimpleAudioVolume;
            _meter = control.AudioMeterInformation;
            DeviceId = deviceId ?? String.Empty;

            ProcessId = unchecked((int)_control.GetProcessID);
            ProcessName = GetProcessName(ProcessId);

            string displayName = _control.DisplayName;
            if (!String.IsNullOrWhiteSpace(displayName))
                DisplayName = displayName.Trim();
            else
                DisplayName = ProcessName.Length > 0 ? ProcessName + ".exe" : "未知音频会话";

            IsSystemSounds = _control.IsSystemSoundsSession;

            string instanceId = _control.GetSessionInstanceIdentifier;
            if (String.IsNullOrWhiteSpace(instanceId))
                instanceId = (_control.GetSessionIdentifier ?? String.Empty) + "|" + ProcessId + "|" + fallbackIndex;
            Id = DeviceId + "|" + instanceId;
        }

        public float ReadPeak()
        {
            return _meter.MasterPeakValue;
        }

        public float ReadVolume()
        {
            return _volume.Volume;
        }

        public void WriteVolume(float volume)
        {
            volume = Math.Max(0.0f, Math.Min(1.0f, volume));
            _volume.Volume = volume;
        }

        public AudioSessionInfo ToInfo()
        {
            float peak = 0.0f;
            float volume = 0.0f;
            try { peak = ReadPeak(); } catch { }
            try { volume = ReadVolume(); } catch { }
            return new AudioSessionInfo
            {
                Id = Id,
                ProcessId = ProcessId,
                ProcessName = ProcessName,
                DisplayName = DisplayName,
                DeviceName = ShortDeviceName(DeviceId),
                Peak = peak,
                Volume = volume,
                IsSystemSounds = IsSystemSounds
            };
        }

        public void Dispose()
        {
            _meter = null;
            if (_volume != null)
            {
                try { _volume.Dispose(); } catch { }
                _volume = null;
            }
            if (_control != null)
            {
                try { _control.Dispose(); } catch { }
                _control = null;
            }
        }

        private static string GetProcessName(int processId)
        {
            if (processId <= 0) return String.Empty;
            try
            {
                using (Process process = Process.GetProcessById(processId))
                    return AppSettings.NormalizeProcessName(process.ProcessName);
            }
            catch
            {
                return "pid-" + processId;
            }
        }

        private static string ShortDeviceName(string value)
        {
            if (String.IsNullOrEmpty(value)) return "默认输出";
            if (value.Length <= 22) return value;
            return "…" + value.Substring(value.Length - 21);
        }

    }

    internal sealed class AudioSessionGraph : IDisposable
    {
        private MMDeviceEnumerator _deviceEnumerator;
        private readonly Dictionary<string, AudioSessionHandle> _sessions =
            new Dictionary<string, AudioSessionHandle>(StringComparer.OrdinalIgnoreCase);

        public AudioSessionGraph()
        {
            _deviceEnumerator = new MMDeviceEnumerator();
        }

        public IList<IDuckableSession> Sessions
        {
            get
            {
                var result = new List<IDuckableSession>();
                foreach (AudioSessionHandle session in _sessions.Values) result.Add(session);
                return result;
            }
        }

        public void Refresh()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MMDeviceCollection devices = _deviceEnumerator.EnumerateAudioEndPoints(
                DataFlow.Render,
                DeviceState.Active);
            for (int deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
            {
                using (MMDevice device = devices[deviceIndex])
                    ReadDevice(device, seen);
            }

            var stale = new List<string>();
            foreach (string id in _sessions.Keys)
                if (!seen.Contains(id)) stale.Add(id);
            foreach (string id in stale)
            {
                _sessions[id].Dispose();
                _sessions.Remove(id);
            }
        }

        public List<AudioSessionInfo> GetInfos()
        {
            var result = new List<AudioSessionInfo>();
            foreach (AudioSessionHandle session in _sessions.Values)
                result.Add(session.ToInfo());
            return result;
        }

        public void Dispose()
        {
            foreach (AudioSessionHandle session in _sessions.Values) session.Dispose();
            _sessions.Clear();
            if (_deviceEnumerator != null)
            {
                try { _deviceEnumerator.Dispose(); } catch { }
                _deviceEnumerator = null;
            }
        }

        private void ReadDevice(MMDevice device, HashSet<string> seen)
        {
            AudioSessionManager manager = null;
            try
            {
                string deviceId = device.ID;
                manager = device.AudioSessionManager;
                manager.RefreshSessions();
                SessionCollection sessions = manager.Sessions;
                for (int sessionIndex = 0; sessionIndex < sessions.Count; sessionIndex++)
                {
                    AudioSessionControl sessionControl = null;
                    AudioSessionHandle candidate = null;
                    try
                    {
                        sessionControl = sessions[sessionIndex];
                        if (sessionControl == null) continue;
                        candidate = new AudioSessionHandle(sessionControl, deviceId, sessionIndex);
                        sessionControl = null;
                        if (String.IsNullOrEmpty(candidate.ProcessName) && !candidate.IsSystemSounds)
                            continue;

                        seen.Add(candidate.Id);
                        if (_sessions.ContainsKey(candidate.Id))
                        {
                            candidate.Dispose();
                            candidate = null;
                        }
                        else
                        {
                            _sessions[candidate.Id] = candidate;
                            candidate = null;
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        if (candidate != null) candidate.Dispose();
                        if (sessionControl != null)
                        {
                            try { sessionControl.Dispose(); } catch { }
                        }
                    }
                }
            }
            finally
            {
                if (manager != null)
                {
                    try { manager.Dispose(); } catch { }
                }
            }
        }
    }

    internal sealed class AudioEngineService : IDisposable
    {
        private readonly object _sync = new object();
        private readonly DuckingCoordinator _coordinator = new DuckingCoordinator();
        private Thread _thread;
        private volatile bool _running;
        private volatile bool _refreshRequested;
        private AppSettings _settings;
        private Func<float> _activeLocalVoicePeakProvider;
        private List<AudioSessionInfo> _snapshot = new List<AudioSessionInfo>();
        private EngineStatus _status = new EngineStatus { TriggerPeakDb = -96.0f };
        private CallAudioActivity _callActivity = new CallAudioActivity { PeakDb = -96.0f };
        private AudioSessionGraph _graph;

        public AudioEngineService(AppSettings settings)
        {
            _settings = settings.Clone();
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(WorkerLoop);
            _thread.Name = "VoiceDuck Audio Engine";
            _thread.IsBackground = true;
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        public void UpdateSettings(AppSettings settings)
        {
            if (settings == null) return;
            lock (_sync) _settings = settings.Clone();
        }

        public void SetActiveLocalVoicePeakProvider(Func<float> provider)
        {
            lock (_sync) _activeLocalVoicePeakProvider = provider;
        }

        public void RequestRefresh()
        {
            _refreshRequested = true;
        }

        public List<AudioSessionInfo> GetSessions()
        {
            lock (_sync) return new List<AudioSessionInfo>(_snapshot);
        }

        public EngineStatus GetStatus()
        {
            lock (_sync) return _status.Clone();
        }

        public CallAudioActivity GetCallActivity()
        {
            lock (_sync) return _callActivity.Clone();
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
            int hr = CoInitializeEx(IntPtr.Zero, 0);
            bool shouldUninitialize = hr >= 0;
            int refreshElapsed = 250;
            int snapshotElapsed = 200;
            try
            {
                _graph = new AudioSessionGraph();
                while (_running)
                {
                    AppSettings settings;
                    Func<float> activeLocalVoicePeakProvider;
                    lock (_sync)
                    {
                        settings = _settings.Clone();
                        activeLocalVoicePeakProvider = _activeLocalVoicePeakProvider;
                    }
                    try
                    {
                        if (refreshElapsed >= 250 || _refreshRequested)
                        {
                            _graph.Refresh();
                            refreshElapsed = 0;
                            _refreshRequested = false;
                        }

                        IList<IDuckableSession> sessions = _graph.Sessions;
                        CallAudioActivity callActivity = MeasureCallActivity(sessions, settings.TriggerApps);
                        float activeLocalVoicePeak = -1.0f;
                        if (activeLocalVoicePeakProvider != null)
                        {
                            try { activeLocalVoicePeak = activeLocalVoicePeakProvider(); }
                            catch { activeLocalVoicePeak = -1.0f; }
                        }
                        _coordinator.Tick(sessions, settings, 50, activeLocalVoicePeak);

                        lock (_sync) _callActivity = callActivity;

                        if (snapshotElapsed >= 200)
                        {
                            List<AudioSessionInfo> infos = _graph.GetInfos();
                            lock (_sync)
                            {
                                _snapshot = infos;
                                _status = new EngineStatus
                                {
                                    Enabled = settings.Enabled || activeLocalVoicePeak >= 0.0f,
                                    Ducking = _coordinator.IsDucking,
                                    TriggerProcess = _coordinator.TriggerProcess,
                                    TriggerPeakDb = _coordinator.TriggerPeakDb,
                                    SessionCount = infos.Count,
                                    LastError = String.Empty
                                };
                            }
                            snapshotElapsed = 0;
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (_sync)
                        {
                            _status.LastError = ex.Message;
                            _status.Enabled = settings.Enabled;
                            _status.Ducking = false;
                            _callActivity = new CallAudioActivity
                            {
                                ProcessName = "音频检测故障",
                                PeakDb = 0.0f
                            };
                        }
                    }

                    Thread.Sleep(50);
                    refreshElapsed += 50;
                    snapshotElapsed += 50;
                }
            }
            catch (Exception ex)
            {
                lock (_sync) _status.LastError = ex.Message;
            }
            finally
            {
                if (_graph != null)
                {
                    try { _coordinator.RestoreNow(_graph.Sessions); } catch { }
                    _graph.Dispose();
                    _graph = null;
                }
                if (shouldUninitialize) CoUninitialize();
            }
        }

        private static CallAudioActivity MeasureCallActivity(
            IList<IDuckableSession> sessions,
            IEnumerable<string> configuredNames)
        {
            var result = new CallAudioActivity
            {
                ProcessName = String.Empty,
                PeakDb = -96.0f
            };
            if (sessions == null) return result;

            foreach (IDuckableSession session in sessions)
            {
                if (session == null ||
                    !MusicShareCore.LooksLikeCallProcess(session.ProcessName, configuredNames))
                    continue;
                try
                {
                    float peakDb = VoiceGate.LinearToDb(session.ReadPeak());
                    if (peakDb > result.PeakDb)
                    {
                        result.ProcessName = AppSettings.NormalizeProcessName(session.ProcessName);
                        result.PeakDb = peakDb;
                    }
                }
                catch
                {
                    result.ProcessName = AppSettings.NormalizeProcessName(session.ProcessName);
                    result.PeakDb = 0.0f;
                    break;
                }
            }
            return result;
        }

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();
    }

    internal enum EDataFlow
    {
        Render = 0,
        Capture = 1,
        All = 2
    }

    [Flags]
    internal enum DeviceStateMask : uint
    {
        Active = 0x00000001,
        Disabled = 0x00000002,
        NotPresent = 0x00000004,
        Unplugged = 0x00000008,
        All = Active | Disabled | NotPresent | Unplugged
    }

}
