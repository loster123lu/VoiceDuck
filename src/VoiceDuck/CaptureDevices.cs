using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
        private static readonly PropertyKey EndpointNameKey = new PropertyKey
        {
            FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
            PropertyId = 14
        };

        public static List<CaptureEndpointInfo> GetCaptureEndpoints()
        {
            var result = new List<CaptureEndpointInfo>();
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection collection = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
                Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(EDataFlow.Capture, DeviceStateMask.All, out collection));
                uint count;
                Marshal.ThrowExceptionForHR(collection.GetCount(out count));
                for (uint index = 0; index < count; index++)
                {
                    IMMDevice device = null;
                    IPropertyStore properties = null;
                    try
                    {
                        Marshal.ThrowExceptionForHR(collection.Item(index, out device));
                        string id;
                        DeviceStateMask state;
                        Marshal.ThrowExceptionForHR(device.GetId(out id));
                        Marshal.ThrowExceptionForHR(device.GetState(out state));

                        string name = String.Empty;
                        if (device.OpenPropertyStore(0, out properties) >= 0 && properties != null)
                            name = ReadStringProperty(properties, EndpointNameKey);
                        if (String.IsNullOrWhiteSpace(name)) name = "录音设备 " + (index + 1);

                        result.Add(new CaptureEndpointInfo
                        {
                            Id = id,
                            Name = name,
                            State = state,
                            IsLoopback = MusicShareCore.IsLoopbackCaptureName(name)
                        });
                    }
                    finally
                    {
                        ReleaseComObject(properties);
                        ReleaseComObject(device);
                    }
                }
            }
            finally
            {
                ReleaseComObject(collection);
                ReleaseComObject(enumerator);
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
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IPropertyStore properties = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
                if (enumerator.GetDefaultAudioEndpoint(dataFlow, role, out device) < 0 || device == null) return String.Empty;
                if (device.OpenPropertyStore(0, out properties) < 0 || properties == null) return String.Empty;
                return ReadStringProperty(properties, EndpointNameKey);
            }
            catch
            {
                return String.Empty;
            }
            finally
            {
                ReleaseComObject(properties);
                ReleaseComObject(device);
                ReleaseComObject(enumerator);
            }
        }

        private static string ReadStringProperty(IPropertyStore store, PropertyKey key)
        {
            PropVariant value;
            int hr = store.GetValue(ref key, out value);
            if (hr < 0) return String.Empty;
            try { return value.GetString(); }
            finally { PropVariantClear(ref value); }
        }

        private static void ReleaseComObject(object value)
        {
            if (value == null || !Marshal.IsComObject(value)) return;
            try { Marshal.FinalReleaseComObject(value); } catch { }
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PropVariant
    {
        [FieldOffset(0)] private ushort _variantType;
        [FieldOffset(8)] private IntPtr _pointerValue;

        public string GetString()
        {
            if (_pointerValue == IntPtr.Zero) return String.Empty;
            if (_variantType == 31) return Marshal.PtrToStringUni(_pointerValue) ?? String.Empty;
            if (_variantType == 8) return Marshal.PtrToStringBSTR(_pointerValue) ?? String.Empty;
            return String.Empty;
        }
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint propertyCount);
        [PreserveSig] int GetAt(uint propertyIndex, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
}
