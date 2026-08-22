using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace VoiceDuck
{
    internal sealed class VirtualCableStatus
    {
        public bool Installed { get; set; }
        public bool Ready { get; set; }
        public string RenderId { get; set; }
        public string RenderName { get; set; }
        public string CaptureId { get; set; }
        public string CaptureName { get; set; }
        public string Message { get; set; }
    }

    internal static class AudioEndpointCatalog
    {
        public static List<AudioEndpointChoice> GetPhysicalCaptureEndpoints()
        {
            return Enumerate(DataFlow.Capture, false);
        }

        public static List<AudioEndpointChoice> GetPhysicalRenderEndpoints()
        {
            return Enumerate(DataFlow.Render, false);
        }

        public static VirtualCableStatus GetVirtualCableStatus()
        {
            AudioEndpointChoice render = FindEndpoint(DataFlow.Render, true);
            AudioEndpointChoice capture = FindEndpoint(DataFlow.Capture, true);
            bool installed = render != null || capture != null;
            bool ready = render != null && capture != null;
            return new VirtualCableStatus
            {
                Installed = installed,
                Ready = ready,
                RenderId = render == null ? String.Empty : render.Id,
                RenderName = render == null ? String.Empty : render.Name,
                CaptureId = capture == null ? String.Empty : capture.Id,
                CaptureName = capture == null ? String.Empty : capture.Name,
                Message = ready
                    ? "VB-CABLE 已就绪：CABLE Input 与 CABLE Output 均可用。"
                    : installed
                        ? "只检测到 VB-CABLE 的一个端点，需要重启 Windows 后再检查。"
                        : "尚未安装内置的 VB-CABLE 驱动。"
            };
        }

        public static MMDevice OpenDevice(string endpointId)
        {
            if (String.IsNullOrWhiteSpace(endpointId))
                throw new ArgumentException("音频设备 ID 不能为空。", "endpointId");
            using (var enumerator = new MMDeviceEnumerator())
                return enumerator.GetDevice(endpointId);
        }

        private static List<AudioEndpointChoice> Enumerate(DataFlow flow, bool virtualOnly)
        {
            var result = new List<AudioEndpointChoice>();
            string defaultId = GetDefaultId(flow);
            using (var enumerator = new MMDeviceEnumerator())
            {
                MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
                foreach (MMDevice device in devices)
                {
                    try
                    {
                        string name = device.FriendlyName ?? String.Empty;
                        bool isCable = flow == DataFlow.Render
                            ? MusicShareCore.IsVbCableRenderName(name)
                            : MusicShareCore.IsVbCableCaptureName(name);
                        bool isVirtual = MusicShareCore.IsVirtualCableName(name);
                        if ((virtualOnly && !isCable) || (!virtualOnly && isVirtual)) continue;
                        result.Add(new AudioEndpointChoice
                        {
                            Id = device.ID,
                            Name = name,
                            IsDefault = String.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase),
                            IsVirtual = isVirtual
                        });
                    }
                    finally { device.Dispose(); }
                }
            }
            result.Sort(delegate(AudioEndpointChoice left, AudioEndpointChoice right)
            {
                if (left.IsDefault != right.IsDefault) return left.IsDefault ? -1 : 1;
                return StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
            });
            return result;
        }

        private static AudioEndpointChoice FindEndpoint(DataFlow flow, bool virtualOnly)
        {
            List<AudioEndpointChoice> endpoints = Enumerate(flow, virtualOnly);
            return endpoints.Count == 0 ? null : endpoints[0];
        }

        private static string GetDefaultId(DataFlow flow)
        {
            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                using (MMDevice device = enumerator.GetDefaultAudioEndpoint(flow, Role.Communications))
                    return device.ID;
            }
            catch
            {
                try
                {
                    using (var enumerator = new MMDeviceEnumerator())
                    using (MMDevice device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia))
                        return device.ID;
                }
                catch { return String.Empty; }
            }
        }
    }
}
