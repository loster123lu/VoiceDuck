using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using NAudio.CoreAudioApi;

namespace VoiceDuck
{
    internal enum DefaultMicrophoneRole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2
    }

    internal sealed class MicrophoneRouteResult
    {
        public bool Succeeded { get; set; }
        public bool Changed { get; set; }
        public string Message { get; set; }

        public static MicrophoneRouteResult Success(bool changed, string message)
        {
            return new MicrophoneRouteResult { Succeeded = true, Changed = changed, Message = message };
        }

        public static MicrophoneRouteResult Failure(string message)
        {
            return new MicrophoneRouteResult { Succeeded = false, Message = message };
        }
    }

    internal interface IDefaultMicrophoneSwitcher
    {
        bool HasPendingRestore { get; }
        MicrophoneRouteResult SwitchTo(string endpointId);
        MicrophoneRouteResult Restore();
    }

    internal interface IDefaultCaptureEndpointController
    {
        string GetDefaultEndpointId(DefaultMicrophoneRole role);
        void SetDefaultEndpoint(string endpointId, DefaultMicrophoneRole role);
        bool IsActiveCaptureEndpoint(string endpointId);
        string FindFallbackPhysicalCaptureEndpoint(string excludedEndpointId);
        void ProbePolicyAccess();
    }

    [DataContract]
    internal sealed class DefaultMicrophoneSnapshot
    {
        [DataMember] public int Version { get; set; }
        [DataMember] public string ManagedEndpointId { get; set; }
        [DataMember] public string ConsoleEndpointId { get; set; }
        [DataMember] public string MultimediaEndpointId { get; set; }
        [DataMember] public string CommunicationsEndpointId { get; set; }

        public string GetEndpointId(DefaultMicrophoneRole role)
        {
            if (role == DefaultMicrophoneRole.Console) return ConsoleEndpointId;
            if (role == DefaultMicrophoneRole.Multimedia) return MultimediaEndpointId;
            return CommunicationsEndpointId;
        }
    }

    internal sealed class DefaultMicrophoneSwitcher : IDefaultMicrophoneSwitcher, IDisposable
    {
        private static readonly DefaultMicrophoneRole[] Roles =
        {
            DefaultMicrophoneRole.Console,
            DefaultMicrophoneRole.Multimedia,
            DefaultMicrophoneRole.Communications
        };

        private readonly object _sync = new object();
        private readonly IDefaultCaptureEndpointController _controller;
        private readonly string _recoveryPath;
        private DefaultMicrophoneSnapshot _snapshot;

        public DefaultMicrophoneSwitcher(
            IDefaultCaptureEndpointController controller,
            string recoveryPath)
        {
            if (controller == null) throw new ArgumentNullException("controller");
            if (String.IsNullOrWhiteSpace(recoveryPath)) throw new ArgumentException("恢复文件路径不能为空。", "recoveryPath");
            _controller = controller;
            _recoveryPath = recoveryPath;
        }

        public bool HasPendingRestore
        {
            get
            {
                lock (_sync) return _snapshot != null || File.Exists(_recoveryPath);
            }
        }

        public MicrophoneRouteResult SwitchTo(string endpointId)
        {
            lock (_sync)
            {
                if (String.IsNullOrWhiteSpace(endpointId))
                    return MicrophoneRouteResult.Failure("没有找到 CABLE Output 的系统设备 ID。");

                if (_snapshot != null || File.Exists(_recoveryPath))
                {
                    MicrophoneRouteResult pendingRestore = RestoreLocked();
                    if (!pendingRestore.Succeeded)
                        return MicrophoneRouteResult.Failure(
                            "上一次默认麦克风尚未恢复，已取消本次切换。\r\n" + pendingRestore.Message);
                }

                try
                {
                    if (!_controller.IsActiveCaptureEndpoint(endpointId))
                        return MicrophoneRouteResult.Failure("CABLE Output 当前不可用，未更改系统默认麦克风。");
                    _controller.ProbePolicyAccess();

                    var snapshot = new DefaultMicrophoneSnapshot
                    {
                        Version = 1,
                        ManagedEndpointId = endpointId,
                        ConsoleEndpointId = _controller.GetDefaultEndpointId(DefaultMicrophoneRole.Console),
                        MultimediaEndpointId = _controller.GetDefaultEndpointId(DefaultMicrophoneRole.Multimedia),
                        CommunicationsEndpointId = _controller.GetDefaultEndpointId(DefaultMicrophoneRole.Communications)
                    };
                    SaveSnapshot(snapshot);
                    _snapshot = snapshot;

                    bool changed = false;
                    foreach (DefaultMicrophoneRole role in Roles)
                    {
                        string current = snapshot.GetEndpointId(role);
                        if (EndpointEquals(current, endpointId)) continue;
                        _controller.SetDefaultEndpoint(endpointId, role);
                        changed = true;
                    }

                    foreach (DefaultMicrophoneRole role in Roles)
                    {
                        if (!EndpointEquals(_controller.GetDefaultEndpointId(role), endpointId))
                            throw new InvalidOperationException("Windows 未确认默认麦克风切换。");
                    }

                    return MicrophoneRouteResult.Success(
                        changed,
                        changed
                            ? "系统默认麦克风已切换到 CABLE Output；停止分享后会自动恢复。"
                            : "系统默认麦克风已经是 CABLE Output；停止分享时不会覆盖原设置。");
                }
                catch (Exception exception)
                {
                    MicrophoneRouteResult rollback = RestoreLocked();
                    string detail = "无法自动切换系统默认麦克风：" + exception.Message;
                    if (!rollback.Succeeded) detail += "\r\n自动回滚也未完成：" + rollback.Message;
                    return MicrophoneRouteResult.Failure(detail);
                }
            }
        }

        public MicrophoneRouteResult Restore()
        {
            lock (_sync) return RestoreLocked();
        }

        public void Dispose()
        {
            try { Restore(); } catch { }
        }

        private MicrophoneRouteResult RestoreLocked()
        {
            DefaultMicrophoneSnapshot snapshot = _snapshot;
            if (snapshot == null)
            {
                try { snapshot = LoadSnapshot(); }
                catch (Exception exception)
                {
                    return MicrophoneRouteResult.Failure("无法读取麦克风恢复记录：" + exception.Message);
                }
            }
            if (snapshot == null)
                return MicrophoneRouteResult.Success(false, "无需恢复系统默认麦克风。");

            var errors = new List<string>();
            bool changed = false;
            foreach (DefaultMicrophoneRole role in Roles)
            {
                try
                {
                    string current = _controller.GetDefaultEndpointId(role);
                    if (!EndpointEquals(current, snapshot.ManagedEndpointId)) continue;

                    string target = snapshot.GetEndpointId(role);
                    if (EndpointEquals(target, snapshot.ManagedEndpointId)) continue;
                    if (String.IsNullOrWhiteSpace(target) || !_controller.IsActiveCaptureEndpoint(target))
                        target = _controller.FindFallbackPhysicalCaptureEndpoint(snapshot.ManagedEndpointId);
                    if (String.IsNullOrWhiteSpace(target))
                    {
                        errors.Add(RoleName(role) + "没有可恢复的真实麦克风");
                        continue;
                    }

                    _controller.SetDefaultEndpoint(target, role);
                    if (!EndpointEquals(_controller.GetDefaultEndpointId(role), target))
                    {
                        errors.Add(RoleName(role) + "恢复后未通过系统确认");
                        continue;
                    }
                    changed = true;
                }
                catch (Exception exception)
                {
                    errors.Add(RoleName(role) + "：" + exception.Message);
                }
            }

            if (errors.Count > 0)
                return MicrophoneRouteResult.Failure(String.Join("；", errors.ToArray()));

            _snapshot = null;
            DeleteSnapshot();
            return MicrophoneRouteResult.Success(
                changed,
                changed ? "系统默认麦克风已恢复。" : "默认麦克风未被覆盖，无需恢复。");
        }

        private void SaveSnapshot(DefaultMicrophoneSnapshot snapshot)
        {
            string folder = Path.GetDirectoryName(_recoveryPath);
            if (!String.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
            string temporaryPath = _recoveryPath + ".tmp";
            using (FileStream stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var serializer = new DataContractJsonSerializer(typeof(DefaultMicrophoneSnapshot));
                serializer.WriteObject(stream, snapshot);
                stream.Flush(true);
            }

            if (File.Exists(_recoveryPath))
            {
                try { File.Replace(temporaryPath, _recoveryPath, null, true); }
                catch
                {
                    File.Copy(temporaryPath, _recoveryPath, true);
                    File.Delete(temporaryPath);
                }
            }
            else
            {
                File.Move(temporaryPath, _recoveryPath);
            }
        }

        private DefaultMicrophoneSnapshot LoadSnapshot()
        {
            if (!File.Exists(_recoveryPath)) return null;
            using (FileStream stream = File.OpenRead(_recoveryPath))
            {
                var serializer = new DataContractJsonSerializer(typeof(DefaultMicrophoneSnapshot));
                var snapshot = serializer.ReadObject(stream) as DefaultMicrophoneSnapshot;
                if (snapshot == null || snapshot.Version != 1 || String.IsNullOrWhiteSpace(snapshot.ManagedEndpointId))
                    throw new InvalidDataException("恢复记录格式无效。");
                return snapshot;
            }
        }

        private void DeleteSnapshot()
        {
            try { if (File.Exists(_recoveryPath)) File.Delete(_recoveryPath); }
            catch { }
            try { if (File.Exists(_recoveryPath + ".tmp")) File.Delete(_recoveryPath + ".tmp"); }
            catch { }
        }

        private static bool EndpointEquals(string left, string right)
        {
            return !String.IsNullOrWhiteSpace(left) && !String.IsNullOrWhiteSpace(right) &&
                String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string RoleName(DefaultMicrophoneRole role)
        {
            if (role == DefaultMicrophoneRole.Console) return "普通默认输入";
            if (role == DefaultMicrophoneRole.Multimedia) return "媒体默认输入";
            return "通信默认输入";
        }
    }

    internal sealed class NoOpDefaultMicrophoneSwitcher : IDefaultMicrophoneSwitcher
    {
        public bool HasPendingRestore { get { return false; } }
        public MicrophoneRouteResult SwitchTo(string endpointId)
        {
            return MicrophoneRouteResult.Success(false, "测试环境未切换默认麦克风。");
        }
        public MicrophoneRouteResult Restore()
        {
            return MicrophoneRouteResult.Success(false, "测试环境无需恢复默认麦克风。");
        }
    }

    internal sealed class DefaultCaptureEndpointController : IDefaultCaptureEndpointController
    {
        public string GetDefaultEndpointId(DefaultMicrophoneRole role)
        {
            using (var enumerator = new MMDeviceEnumerator())
            using (MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, ToNaudioRole(role)))
                return device.ID;
        }

        public void SetDefaultEndpoint(string endpointId, DefaultMicrophoneRole role)
        {
            IPolicyConfig policy = null;
            try
            {
                policy = (IPolicyConfig)new PolicyConfigComObject();
                int result = policy.SetDefaultEndpoint(endpointId, (int)role);
                Marshal.ThrowExceptionForHR(result);
            }
            finally
            {
                if (policy != null && Marshal.IsComObject(policy))
                    Marshal.FinalReleaseComObject(policy);
            }
        }

        public bool IsActiveCaptureEndpoint(string endpointId)
        {
            if (String.IsNullOrWhiteSpace(endpointId)) return false;
            using (var enumerator = new MMDeviceEnumerator())
            {
                MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                foreach (MMDevice device in devices)
                {
                    try
                    {
                        if (String.Equals(device.ID, endpointId, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    finally { device.Dispose(); }
                }
            }
            return false;
        }

        public string FindFallbackPhysicalCaptureEndpoint(string excludedEndpointId)
        {
            using (var enumerator = new MMDeviceEnumerator())
            {
                MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                foreach (MMDevice device in devices)
                {
                    try
                    {
                        string name = device.FriendlyName ?? String.Empty;
                        if (!String.Equals(device.ID, excludedEndpointId, StringComparison.OrdinalIgnoreCase) &&
                            !MusicShareCore.IsVirtualCableName(name) &&
                            !MusicShareCore.IsLoopbackCaptureName(name))
                            return device.ID;
                    }
                    finally { device.Dispose(); }
                }
            }
            return String.Empty;
        }

        public void ProbePolicyAccess()
        {
            IPolicyConfig policy = null;
            try { policy = (IPolicyConfig)new PolicyConfigComObject(); }
            finally
            {
                if (policy != null && Marshal.IsComObject(policy))
                    Marshal.FinalReleaseComObject(policy);
            }
        }

        private static Role ToNaudioRole(DefaultMicrophoneRole role)
        {
            if (role == DefaultMicrophoneRole.Console) return Role.Console;
            if (role == DefaultMicrophoneRole.Multimedia) return Role.Multimedia;
            return Role.Communications;
        }
    }

    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    internal class PolicyConfigComObject
    {
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultPeriod, IntPtr defaultValue, IntPtr minimumValue);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr propertyKey, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr propertyKey, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
    }
}
