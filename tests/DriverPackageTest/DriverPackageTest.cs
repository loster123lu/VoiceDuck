using System;

namespace VoiceDuck
{
    internal static class DriverPackageTest
    {
        private static void Main()
        {
            try
            {
                var installer = new VirtualAudioInstaller();
                if (!installer.EmbeddedPackageAvailable)
                    throw new InvalidOperationException("Embedded VB-CABLE package is missing.");
                string hash = installer.VerifyEmbeddedPackage();
                Console.WriteLine("VBCABLE_PACKAGE_VERIFIED=True");
                Console.WriteLine("VBCABLE_PACKAGE_SHA256=" + hash);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("ERROR_TYPE=" + exception.GetType().FullName);
                Console.Error.WriteLine("ERROR_MESSAGE=" + exception.Message);
                Environment.ExitCode = 2;
            }
        }
    }
}
