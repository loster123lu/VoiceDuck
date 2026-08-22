using System;
using System.Diagnostics;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;

namespace VoiceDuck
{
    internal sealed class AudioRecoveryResult
    {
        public bool Succeeded { get; set; }
        public string Message { get; set; }
    }

    internal static class AudioRecoveryUtility
    {
        public const string CommandLineSwitch = "--recover-windows-audio";

        public static bool IsRecoveryRequest(string[] arguments)
        {
            if (arguments == null) return false;
            foreach (string argument in arguments)
                if (String.Equals(argument, CommandLineSwitch, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static int RunElevatedRecoveryMode()
        {
            try
            {
                EnsureServiceRunning("AudioEndpointBuilder", 20000);
                using (var audio = new ServiceController("Audiosrv"))
                {
                    audio.Refresh();
                    if (audio.Status == ServiceControllerStatus.Running)
                    {
                        audio.Stop();
                        audio.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                    }
                    else if (audio.Status == ServiceControllerStatus.StopPending)
                    {
                        audio.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                    }

                    audio.Refresh();
                    if (audio.Status != ServiceControllerStatus.Running &&
                        audio.Status != ServiceControllerStatus.StartPending)
                        audio.Start();
                    audio.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(25));
                }
                return 0;
            }
            catch (Exception exception)
            {
                try { Console.Error.WriteLine(exception.Message); } catch { }
                return 1;
            }
        }

        public static AudioRecoveryResult RecoverWithElevation()
        {
            if (WaitForAudioReady(1500, 750))
            {
                return new AudioRecoveryResult
                {
                    Succeeded = true,
                    Message = "Windows Audio 服务和真实输入输出设备已经正常，无需恢复。"
                };
            }

            try
            {
                string executable = Assembly.GetEntryAssembly().Location;
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = CommandLineSwitch,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null) throw new InvalidOperationException("无法启动音频恢复程序。");
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                        throw new InvalidOperationException("Windows Audio 服务恢复程序返回代码 " + process.ExitCode + "。");
                }

                bool ready = WaitForAudioReady(30000, 3000);
                return new AudioRecoveryResult
                {
                    Succeeded = ready,
                    Message = ready
                        ? "声音已经恢复，真实麦克风和扬声器均重新出现；系统默认设备没有被 VoiceDuck 修改。"
                        : "Windows Audio 服务已启动，但设备仍未完整出现。请等待几秒后再次点击“恢复声音”；仍无效时再重启 Windows。"
                };
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                return new AudioRecoveryResult
                {
                    Succeeded = false,
                    Message = exception.NativeErrorCode == 1223
                        ? "已取消管理员授权，没有重启 Windows Audio 服务。"
                        : "无法启动音频恢复：" + exception.Message
                };
            }
            catch (Exception exception)
            {
                return new AudioRecoveryResult
                {
                    Succeeded = false,
                    Message = "音频恢复未完成：" + exception.Message
                };
            }
        }

        public static bool WaitForAudioReady(int timeoutMilliseconds, int stableMilliseconds)
        {
            Stopwatch timer = Stopwatch.StartNew();
            long readySince = -1;
            while (timer.ElapsedMilliseconds < timeoutMilliseconds)
            {
                if (IsAudioReady())
                {
                    if (readySince < 0) readySince = timer.ElapsedMilliseconds;
                    if (timer.ElapsedMilliseconds - readySince >= stableMilliseconds) return true;
                }
                else
                {
                    readySince = -1;
                }
                Thread.Sleep(250);
            }
            return false;
        }

        public static bool IsAudioReady()
        {
            try
            {
                using (var audio = new ServiceController("Audiosrv"))
                {
                    if (audio.Status != ServiceControllerStatus.Running) return false;
                }
                return AudioEndpointCatalog.GetPhysicalCaptureEndpoints().Count > 0 &&
                       AudioEndpointCatalog.GetPhysicalRenderEndpoints().Count > 0 &&
                       !String.IsNullOrWhiteSpace(CaptureDeviceInspector.GetDefaultPhysicalEndpointName(EDataFlow.Capture)) &&
                       !String.IsNullOrWhiteSpace(CaptureDeviceInspector.GetDefaultPhysicalEndpointName(EDataFlow.Render));
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureServiceRunning(string serviceName, int timeoutMilliseconds)
        {
            using (var service = new ServiceController(serviceName))
            {
                service.Refresh();
                if (service.Status != ServiceControllerStatus.Running &&
                    service.Status != ServiceControllerStatus.StartPending)
                    service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromMilliseconds(timeoutMilliseconds));
            }
        }
    }
}
