using System;
using System.Collections.Generic;
using System.Threading;

namespace VoiceDuck
{
    internal static class AudioSmokeTest
    {
        [STAThread]
        private static void Main()
        {
            AudioEngineService service = null;
            try
            {
                service = new AudioEngineService(AppSettings.CreateDefault());
                service.Start();
                Thread.Sleep(600);
                List<AudioSessionInfo> sessions = service.GetSessions();
                Console.WriteLine("AUDIO_SESSIONS=" + sessions.Count);
                foreach (AudioSessionInfo session in sessions)
                {
                    Console.WriteLine(
                        "SESSION=" + session.ProcessName +
                        " PID=" + session.ProcessId +
                        " VOLUME=" + session.Volume.ToString("0.000") +
                        " PEAK_DB=" + session.PeakDb.ToString("0.0"));
                }

                List<CaptureEndpointInfo> captureEndpoints = CaptureDeviceInspector.GetCaptureEndpoints();
                Console.WriteLine("CAPTURE_ENDPOINTS=" + captureEndpoints.Count);
                foreach (CaptureEndpointInfo endpoint in captureEndpoints)
                {
                    Console.WriteLine(
                        "CAPTURE=" + endpoint.Name +
                        " STATE=" + endpoint.State +
                        " LOOPBACK=" + endpoint.IsLoopback);
                }
                Console.WriteLine("DEFAULT_CAPTURE=" + CaptureDeviceInspector.GetDefaultPhysicalEndpointName(EDataFlow.Capture));
                Console.WriteLine("DEFAULT_RENDER=" + CaptureDeviceInspector.GetDefaultPhysicalEndpointName(EDataFlow.Render));

                VirtualCableStatus cable = AudioEndpointCatalog.GetVirtualCableStatus();
                Console.WriteLine("VBCABLE_INSTALLED=" + cable.Installed);
                Console.WriteLine("VBCABLE_READY=" + cable.Ready);
                Console.WriteLine("VBCABLE_RENDER=" + cable.RenderName);
                Console.WriteLine("VBCABLE_CAPTURE=" + cable.CaptureName);
                Console.WriteLine("AUDIO_RECOVERY_READY=" + AudioRecoveryUtility.IsAudioReady());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR_TYPE=" + ex.GetType().FullName);
                Console.Error.WriteLine("ERROR_HRESULT=0x" + ex.HResult.ToString("X8"));
                try { Console.Error.WriteLine("ERROR_MESSAGE=" + ex.Message); } catch { }
                Environment.ExitCode = 2;
            }
            finally
            {
                if (service != null) service.Dispose();
            }
        }
    }
}
