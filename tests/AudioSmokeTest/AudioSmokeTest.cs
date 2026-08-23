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
            HearingProtectionService hearingProtection = null;
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
                var defaultMicrophoneController = new DefaultCaptureEndpointController();
                defaultMicrophoneController.ProbePolicyAccess();
                Console.WriteLine("DEFAULT_MICROPHONE_SWITCH_POLICY=True");
                Console.WriteLine("DEFAULT_MICROPHONE_CONSOLE=" +
                    defaultMicrophoneController.GetDefaultEndpointId(DefaultMicrophoneRole.Console));
                Console.WriteLine("DEFAULT_MICROPHONE_MULTIMEDIA=" +
                    defaultMicrophoneController.GetDefaultEndpointId(DefaultMicrophoneRole.Multimedia));
                Console.WriteLine("DEFAULT_MICROPHONE_COMMUNICATIONS=" +
                    defaultMicrophoneController.GetDefaultEndpointId(DefaultMicrophoneRole.Communications));
                hearingProtection = new HearingProtectionService(AppSettings.CreateDefault());
                hearingProtection.Start();
                Thread.Sleep(150);
                HearingProtectionStatus hearingStatus = hearingProtection.GetStatus();
                Console.WriteLine("HEARING_PROTECTION_PROBE=" +
                    (!String.IsNullOrWhiteSpace(hearingStatus.DeviceName)));
                Console.WriteLine("HEARING_PROTECTION_DEVICE=" + hearingStatus.DeviceName);
                Console.WriteLine("HEARING_PROTECTION_DEFAULT_ENABLED=" + hearingStatus.Enabled);
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
                if (hearingProtection != null) hearingProtection.Dispose();
                if (service != null) service.Dispose();
            }
        }
    }
}
