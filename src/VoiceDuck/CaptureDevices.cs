using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace VoiceDuck
{
    internal sealed class CaptureEndpointInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public DeviceStateMask State { get; set; }
        public bool IsLoopback { get; set; }
        public bool IsActive { get { return (State & DeviceStateMask.Active) != 0; } }
    }

    internal static class CaptureDeviceInspector
    {
        public static List<CaptureEndpointInfo> GetCaptureEndpoints()
        {
            var result = new List<CaptureEndpointInfo>();
            using (var enumerator = new MMDeviceEnumerator())
            {
                MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(
                    DataFlow.Capture,
                    DeviceState.All);
                for (int index = 0; index < devices.Count; index++)
                {
                    using (MMDevice device = devices[index])
                    {
                        try
                        {
                            string name;
                            try { name = device.FriendlyName ?? String.Empty; }
                            catch { name = String.Empty; }
                            if (String.IsNullOrWhiteSpace(name)) name = "录音设备 " + (index + 1);
                            result.Add(new CaptureEndpointInfo
                            {
                                Id = device.ID,
                                Name = name,
                                State = (DeviceStateMask)(uint)device.State,
                                IsLoopback = MusicShareCore.IsLoopbackCaptureName(name)
                            });
                        }
                        catch
                        {
                            // A disconnected Bluetooth or old driver endpoint may
                            // disappear between enumeration and property access.
                        }
                    }
                }
            }

            result.Sort(delegate(CaptureEndpointInfo left, CaptureEndpointInfo right)
            {
                if (left.IsLoopback != right.IsLoopback) return left.IsLoopback ? -1 : 1;
                if (left.IsActive != right.IsActive) return left.IsActive ? -1 : 1;
                return StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
            });
            return result;
        }

        public static string GetDefaultPhysicalEndpointName(EDataFlow dataFlow)
        {
            int[] roles = dataFlow == EDataFlow.Capture ? new[] { 2, 1, 0 } : new[] { 1, 0, 2 };
            foreach (int role in roles)
            {
                string name = GetDefaultEndpointName(dataFlow, role);
                if (!String.IsNullOrWhiteSpace(name) && !MusicShareCore.IsVirtualCableName(name)) return name;
            }
            return String.Empty;
        }

        private static string GetDefaultEndpointName(EDataFlow dataFlow, int role)
        {
            try
            {
                DataFlow flow = dataFlow == EDataFlow.Capture ? DataFlow.Capture : DataFlow.Render;
                using (var enumerator = new MMDeviceEnumerator())
                using (MMDevice device = enumerator.GetDefaultAudioEndpoint(flow, (Role)role))
                    return device.FriendlyName ?? String.Empty;
            }
            catch
            {
                return String.Empty;
            }
        }
    }
}
