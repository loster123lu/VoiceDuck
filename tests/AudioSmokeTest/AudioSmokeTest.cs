using System;
using System.Collections.Generic;

namespace VoiceDuck
{
    internal static class AudioSmokeTest
    {
        private static void Main()
        {
            try
            {
                using (var graph = new AudioSessionGraph())
                {
                    graph.Refresh();
                    List<AudioSessionInfo> sessions = graph.GetInfos();
                    Console.WriteLine("AUDIO_SESSIONS=" + sessions.Count);
                    foreach (AudioSessionInfo session in sessions)
                    {
                        Console.WriteLine(
                            "SESSION=" + session.ProcessName +
                            " PID=" + session.ProcessId +
                            " VOLUME=" + session.Volume.ToString("0.000") +
                            " PEAK_DB=" + session.PeakDb.ToString("0.0"));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR_TYPE=" + ex.GetType().FullName);
                Console.Error.WriteLine("ERROR_HRESULT=0x" + ex.HResult.ToString("X8"));
                try { Console.Error.WriteLine("ERROR_MESSAGE=" + ex.Message); } catch { }
                Environment.ExitCode = 2;
            }
        }
    }
}
