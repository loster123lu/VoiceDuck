using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace VoiceDuck
{
    internal sealed class AudioSessionHandle : IDuckableSession, IDisposable
    {
        private object _sessionObject;
        private IAudioSessionControl2 _control;
        private ISimpleAudioVolume _volume;
        private IAudioMeterInformation _meter;

        public string Id { get; private set; }
        public int ProcessId { get; private set; }
        public string ProcessName { get; private set; }
        public string DisplayName { get; private set; }
        public string DeviceId { get; private set; }
        public bool IsSystemSounds { get; private set; }

        public AudioSessionHandle(object sessionObject, string deviceId, int fallbackIndex)
        {
            _sessionObject = sessionObject;
            _control = (IAudioSessionControl2)sessionObject;
            _volume = (ISimpleAudioVolume)sessionObject;
            _meter = (IAudioMeterInformation)sessionObject;
            DeviceId = deviceId ?? String.Empty;

            uint processId;
            Marshal.ThrowExceptionForHR(_control.GetProcessId(out processId));
            ProcessId = unchecked((int)processId);
            ProcessName = GetProcessName(ProcessId);

            string displayName;
            if (_control.GetDisplayName(out displayName) >= 0 && !String.IsNullOrWhiteSpace(displayName))
                DisplayName = displayName.Trim();
            else
                DisplayName = ProcessName.Length > 0 ? ProcessName + ".exe" : "未知音频会话";

            IsSystemSounds = _control.IsSystemSoundsSession() == 0;

            string instanceId;
            if (_control.GetSessionInstanceIdentifier(out instanceId) < 0 || String.IsNullOrWhiteSpace(instanceId))
            {
                string sessionId;
                _control.GetSessionIdentifier(out sessionId);
                instanceId = (sessionId ?? String.Empty) + "|" + ProcessId + "|" + fallbackIndex;
            }
            Id = DeviceId + "|" + instanceId;
        }

        public float ReadPeak()
        {
            float value;
            Marshal.ThrowExceptionForHR(_meter.GetPeakValue(out value));
            return value;
        }

        public float ReadVolume()
        {
            float value;
            Marshal.ThrowExceptionForHR(_volume.GetMasterVolume(out value));
            return value;
        }

        public void WriteVolume(float volume)
        {
            volume = Math.Max(0.0f, Math.Min(1.0f, volume));
            Guid context = AudioSessionGraph.EventContext;
            Marshal.ThrowExceptionForHR(_volume.SetMasterVolume(volume, ref context));
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
            _control = null;
            _volume = null;
            _meter = null;
            ReleaseComObject(_sessionObject);
            _sessionObject = null;
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

        private static void ReleaseComObject(object value)
        {
            if (value == null || !Marshal.IsComObject(value)) return;
            try { Marshal.FinalReleaseComObject(value); } catch { }
        }
    }

    internal sealed class AudioSessionGraph : IDisposable
    {
        public static readonly Guid EventContext = new Guid("A4748F16-C27C-4B46-8532-B4D7BA7E40B3");

        private IMMDeviceEnumerator _deviceEnumerator;
        private readonly Dictionary<string, AudioSessionHandle> _sessions =
            new Dictionary<string, AudioSessionHandle>(StringComparer.OrdinalIgnoreCase);

        public AudioSessionGraph()
        {
            _deviceEnumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
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
            IMMDeviceCollection devices = null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Marshal.ThrowExceptionForHR(_deviceEnumerator.EnumAudioEndpoints(
                    EDataFlow.Render,
                    DeviceStateMask.Active,
                    out devices));
                uint deviceCount;
                Marshal.ThrowExceptionForHR(devices.GetCount(out deviceCount));
                for (uint deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
                    ReadDevice(devices, deviceIndex, seen);
            }
            finally
            {
                ReleaseComObject(devices);
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
            ReleaseComObject(_deviceEnumerator);
            _deviceEnumerator = null;
        }

        private void ReadDevice(IMMDeviceCollection devices, uint deviceIndex, HashSet<string> seen)
        {
            IMMDevice device = null;
            object managerObject = null;
            IAudioSessionEnumerator sessionEnumerator = null;
            try
            {
                Marshal.ThrowExceptionForHR(devices.Item(deviceIndex, out device));
                string deviceId;
                Marshal.ThrowExceptionForHR(device.GetId(out deviceId));

                Guid managerGuid = typeof(IAudioSessionManager2).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(
                    ref managerGuid,
                    ClsCtx.All,
                    IntPtr.Zero,
                    out managerObject));
                var manager = (IAudioSessionManager2)managerObject;
                Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out sessionEnumerator));

                int sessionCount;
                Marshal.ThrowExceptionForHR(sessionEnumerator.GetCount(out sessionCount));
                for (int sessionIndex = 0; sessionIndex < sessionCount; sessionIndex++)
                {
                    IAudioSessionControl sessionControl = null;
                    AudioSessionHandle candidate = null;
                    try
                    {
                        if (sessionEnumerator.GetSession(sessionIndex, out sessionControl) < 0 || sessionControl == null)
                            continue;
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
                        ReleaseComObject(sessionControl);
                    }
                }
            }
            finally
            {
                ReleaseComObject(sessionEnumerator);
                ReleaseComObject(managerObject);
                ReleaseComObject(device);
            }
        }

        private static void ReleaseComObject(object value)
        {
            if (value == null || !Marshal.IsComObject(value)) return;
            try { Marshal.FinalReleaseComObject(value); } catch { }
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
        private List<AudioSessionInfo> _snapshot = new List<AudioSessionInfo>();
        private EngineStatus _status = new EngineStatus { TriggerPeakDb = -96.0f };
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
            int refreshElapsed = 1000;
            int snapshotElapsed = 200;
            try
            {
                _graph = new AudioSessionGraph();
                while (_running)
                {
                    AppSettings settings;
                    lock (_sync) settings = _settings.Clone();
                    try
                    {
                        if (refreshElapsed >= 1000 || _refreshRequested)
                        {
                            _graph.Refresh();
                            refreshElapsed = 0;
                            _refreshRequested = false;
                        }

                        IList<IDuckableSession> sessions = _graph.Sessions;
                        _coordinator.Tick(sessions, settings, 50);

                        if (snapshotElapsed >= 200)
                        {
                            List<AudioSessionInfo> infos = _graph.GetInfos();
                            lock (_sync)
                            {
                                _snapshot = infos;
                                _status = new EngineStatus
                                {
                                    Enabled = settings.Enabled,
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
        Active = 0x00000001
    }

    [Flags]
    internal enum ClsCtx : uint
    {
        InprocServer = 0x1,
        InprocHandler = 0x2,
        LocalServer = 0x4,
        RemoteServer = 0x10,
        All = InprocServer | InprocHandler | LocalServer | RemoteServer
    }

    internal enum AudioSessionState
    {
        Inactive = 0,
        Active = 1,
        Expired = 2
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal sealed class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, DeviceStateMask stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint deviceNumber, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, ClsCtx clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out DeviceStateMask state);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(ref Guid sessionGuid, uint streamFlags, out IAudioSessionControl sessionControl);
        [PreserveSig] int GetSimpleAudioVolume(ref Guid sessionGuid, uint streamFlags, out ISimpleAudioVolume audioVolume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
        [PreserveSig] int RegisterSessionNotification(IntPtr sessionNotification);
        [PreserveSig] int UnregisterSessionNotification(IntPtr sessionNotification);
        [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);
        [PreserveSig] int UnregisterDuckNotification(IntPtr duckNotification);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int sessionCount);
        [PreserveSig] int GetSession(int sessionCount, out IAudioSessionControl sessionControl);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out AudioSessionState state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingId);
        [PreserveSig] int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
    }

    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out AudioSessionState state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingId);
        [PreserveSig] int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionId);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionInstanceId);
        [PreserveSig] int GetProcessId(out uint processId);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
        [PreserveSig] int GetMeteringChannelCount(out int channelCount);
        [PreserveSig] int GetChannelsPeakValues(int channelCount, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] peakValues);
        [PreserveSig] int QueryHardwareSupport(out int hardwareSupportMask);
    }
}
