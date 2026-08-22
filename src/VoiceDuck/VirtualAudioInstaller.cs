using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace VoiceDuck
{
    internal sealed class DriverActionResult
    {
        public bool Succeeded { get; set; }
        public bool RestartRequired { get; set; }
        public string Message { get; set; }
        public VirtualCableStatus Status { get; set; }
    }

    internal static class CallSafetyInspector
    {
        public static bool HasPotentialCallAudioSession(IEnumerable<string> configuredNames, out string processName)
        {
            processName = String.Empty;
            try
            {
                using (var graph = new AudioSessionGraph())
                {
                    graph.Refresh();
                    foreach (AudioSessionInfo session in graph.GetInfos())
                    {
                        if (session.ProcessId > 0 &&
                            MusicShareCore.LooksLikeCallProcess(session.ProcessName, configuredNames))
                        {
                            processName = session.ProcessName;
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }
    }

    internal sealed class VirtualAudioInstaller
    {
        private const string ResourceName = "VoiceDuck.Resources.VBCABLE_Driver_Pack45.zip";
        private const string ArchiveSha256 = "B950E39F01AF1D04EA623C8F6D8EB9B6EA5C477C637295FABF20631C85116BFB";
        private const string SetupX64Sha256 = "734C35DFA6D98F48782A451633CEB471166EC70D60482FD89A1123D0EE3C4F41";
        private const string SetupX86Sha256 = "01FFC86B623FF3C75A883AA900C0215A89482988E1C8E55988FC0A9FB513DBED";

        public VirtualCableStatus GetStatus()
        {
            return AudioEndpointCatalog.GetVirtualCableStatus();
        }

        public bool EmbeddedPackageAvailable
        {
            get
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                    return stream != null && stream.Length > 0;
            }
        }

        public DriverActionResult Install()
        {
            return RunOfficialSetup(false);
        }

        public DriverActionResult Uninstall()
        {
            return RunOfficialSetup(true);
        }

        public string VerifyEmbeddedPackage()
        {
            string temporaryDirectory = String.Empty;
            try
            {
                byte[] archive = ReadEmbeddedArchive();
                AssertHash(archive, ArchiveSha256, "内置 VB-CABLE 压缩包");
                temporaryDirectory = CreateTemporaryDirectory();
                ExtractArchive(archive, temporaryDirectory);
                string setupName = Environment.Is64BitOperatingSystem
                    ? "VBCABLE_Setup_x64.exe"
                    : "VBCABLE_Setup.exe";
                string setupPath = Path.Combine(temporaryDirectory, setupName);
                AssertFileHash(
                    setupPath,
                    Environment.Is64BitOperatingSystem ? SetupX64Sha256 : SetupX86Sha256,
                    "VB-CABLE 安装程序");
                AuthenticodeVerifier.AssertTrustedVbAudioFile(setupPath);
                return ArchiveSha256;
            }
            finally { TryDeleteTemporaryDirectory(temporaryDirectory); }
        }

        private static DriverActionResult RunOfficialSetup(bool uninstall)
        {
            string temporaryDirectory = String.Empty;
            try
            {
                byte[] archive = ReadEmbeddedArchive();
                AssertHash(archive, ArchiveSha256, "内置 VB-CABLE 压缩包");
                temporaryDirectory = CreateTemporaryDirectory();
                ExtractArchive(archive, temporaryDirectory);

                string setupName = Environment.Is64BitOperatingSystem
                    ? "VBCABLE_Setup_x64.exe"
                    : "VBCABLE_Setup.exe";
                string setupPath = Path.Combine(temporaryDirectory, setupName);
                if (!File.Exists(setupPath))
                    throw new InvalidDataException("内置驱动包缺少 " + setupName + "。");

                AssertFileHash(
                    setupPath,
                    Environment.Is64BitOperatingSystem ? SetupX64Sha256 : SetupX86Sha256,
                    "VB-CABLE 安装程序");
                AuthenticodeVerifier.AssertTrustedVbAudioFile(setupPath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = setupPath,
                    Arguments = uninstall ? "-u -h" : "-i -h",
                    WorkingDirectory = temporaryDirectory,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null) throw new InvalidOperationException("无法启动 VB-CABLE 安装程序。");
                    process.WaitForExit();
                    int exitCode = process.ExitCode;
                    bool audioReady = AudioRecoveryUtility.WaitForAudioReady(25000, 5000);
                    if (exitCode != 0)
                        throw new InvalidOperationException(
                            "VB-CABLE 安装程序返回代码 " + exitCode + "。" +
                            (audioReady ? String.Empty : " Windows Audio 尚未恢复，请点击“恢复声音”。"));

                    VirtualCableStatus status = AudioEndpointCatalog.GetVirtualCableStatus();
                    bool restartRequired = !audioReady || (uninstall ? status.Installed : !status.Ready);
                    return new DriverActionResult
                    {
                        Succeeded = true,
                        RestartRequired = restartRequired,
                        Status = status,
                        Message = !audioReady
                            ? "驱动操作已经结束，但 Windows Audio 和真实设备尚未稳定恢复。请点击“恢复声音”，不要继续通话分享。"
                            : uninstall
                                ? (restartRequired ? "驱动卸载已提交，请在结束通话后重启 Windows。" : "VB-CABLE 已卸载，声音设备已恢复。")
                                : (restartRequired ? "驱动安装完成，声音设备已恢复；VB-CABLE 仍需在稍后重启后生效。" : "VB-CABLE 已安装，声音设备和虚拟线缆均已稳定就绪。")
                    };
                }
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                return new DriverActionResult
                {
                    Succeeded = false,
                    RestartRequired = false,
                    Status = AudioEndpointCatalog.GetVirtualCableStatus(),
                    Message = exception.NativeErrorCode == 1223
                        ? "已取消管理员授权，驱动没有安装或卸载。"
                        : "无法运行驱动安装程序：" + exception.Message
                };
            }
            catch (Exception exception)
            {
                return new DriverActionResult
                {
                    Succeeded = false,
                    RestartRequired = false,
                    Status = AudioEndpointCatalog.GetVirtualCableStatus(),
                    Message = exception.Message
                };
            }
            finally
            {
                TryDeleteTemporaryDirectory(temporaryDirectory);
            }
        }

        private static byte[] ReadEmbeddedArchive()
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (source == null) throw new InvalidDataException("当前构建没有包含 VB-CABLE 驱动包。");
                using (var memory = new MemoryStream())
                {
                    source.CopyTo(memory);
                    return memory.ToArray();
                }
            }
        }

        private static string CreateTemporaryDirectory()
        {
            string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(tempRoot, "VoiceDuck-VBCable-" + Guid.NewGuid().ToString("N")));
            if (!candidate.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                throw new IOException("拒绝在系统临时目录之外展开驱动包。");
            Directory.CreateDirectory(candidate);
            return candidate;
        }

        private static void ExtractArchive(byte[] archiveBytes, string destination)
        {
            string destinationPrefix = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var memory = new MemoryStream(archiveBytes, false))
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Read, false))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string outputPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                    if (!outputPath.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("驱动压缩包包含不安全的路径。");
                    if (String.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(outputPath);
                        continue;
                    }
                    string parent = Path.GetDirectoryName(outputPath);
                    if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    using (Stream input = entry.Open())
                    using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        input.CopyTo(output);
                }
            }
        }

        private static void AssertFileHash(string path, string expected, string label)
        {
            using (var stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                AssertHashValue(sha.ComputeHash(stream), expected, label);
        }

        private static void AssertHash(byte[] bytes, string expected, string label)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(bytes);
            AssertHashValue(hash, expected, label);
        }

        private static void AssertHashValue(byte[] hash, string expected, string label)
        {
            string actual = BitConverter.ToString(hash).Replace("-", String.Empty);
            if (!String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(label + "的 SHA-256 校验失败。");
        }

        private static void TryDeleteTemporaryDirectory(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return;
            try
            {
                string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string target = Path.GetFullPath(path);
                if (target.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(target))
                    Directory.Delete(target, true);
            }
            catch { }
        }
    }

    internal static class AuthenticodeVerifier
    {
        private static readonly Guid GenericVerifyAction = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

        public static void AssertTrustedVbAudioFile(string path)
        {
            WinTrustFileInfo fileInfo = new WinTrustFileInfo(path);
            IntPtr fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
            try
            {
                Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
                var trustData = new WinTrustData(fileInfoPointer);
                Guid action = GenericVerifyAction;
                uint result = WinVerifyTrust(IntPtr.Zero, ref action, ref trustData);
                if (result != 0)
                    throw new InvalidDataException("VB-CABLE 安装程序的 Windows 数字签名无效（0x" + result.ToString("X8") + "）。");
            }
            finally { Marshal.FreeHGlobal(fileInfoPointer); }

            X509Certificate certificate = X509Certificate.CreateFromSignedFile(path);
            using (var certificate2 = new X509Certificate2(certificate))
            {
                string subject = certificate2.Subject ?? String.Empty;
                if (subject.IndexOf("BUREL VINCENT", StringComparison.OrdinalIgnoreCase) < 0 &&
                    subject.IndexOf("Vincent Burel", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidDataException("VB-CABLE 安装程序签名者不是预期的 VB-Audio 发布者。");
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint StructSize;
            [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;

            public WinTrustFileInfo(string path)
            {
                StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo));
                FilePath = path;
                FileHandle = IntPtr.Zero;
                KnownSubject = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;

            public WinTrustData(IntPtr fileInfo)
            {
                StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData));
                PolicyCallbackData = IntPtr.Zero;
                SipClientData = IntPtr.Zero;
                UiChoice = 2;
                RevocationChecks = 0;
                UnionChoice = 1;
                FileInfo = fileInfo;
                StateAction = 0;
                StateData = IntPtr.Zero;
                UrlReference = IntPtr.Zero;
                ProviderFlags = 0x00001000;
                UiContext = 0;
            }
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint WinVerifyTrust(
            IntPtr windowHandle,
            ref Guid actionId,
            ref WinTrustData trustData);
    }
}
