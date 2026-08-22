using System;
using System.IO;
using NAudio.Wave;

namespace VoiceDuck
{
    internal static class MediaDecodeTest
    {
        private static void Main()
        {
            string path = Path.Combine(Path.GetTempPath(), "VoiceDuck-Decode-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                WriteTestWave(path);
                var settings = new MediaFoundationReader.MediaFoundationReaderSettings
                {
                    RequestFloatOutput = true,
                    SingleReaderObject = true,
                    RepositionInRead = false
                };
                using (var reader = new MediaFoundationReader(path, settings))
                using (var resampler = new MediaFoundationResampler(
                    reader,
                    WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)))
                {
                    byte[] buffer = new byte[8192];
                    int read = resampler.Read(buffer, 0, buffer.Length);
                    if (read <= 0) throw new InvalidDataException("Media Foundation returned no decoded audio.");
                    if (resampler.WaveFormat.SampleRate != 48000 || resampler.WaveFormat.Channels != 2)
                        throw new InvalidDataException("Decoded stream does not match the 48 kHz stereo contract.");
                    Console.WriteLine("MEDIA_FOUNDATION_DECODE=True");
                    Console.WriteLine("DECODED_BYTES=" + read);
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("ERROR_TYPE=" + exception.GetType().FullName);
                Console.Error.WriteLine("ERROR_MESSAGE=" + exception.Message);
                Environment.ExitCode = 2;
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        private static void WriteTestWave(string path)
        {
            const int sampleRate = 48000;
            const short channels = 2;
            const short bitsPerSample = 16;
            const int frames = 4800;
            int dataLength = frames * channels * (bitsPerSample / 8);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataLength);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });
                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write(channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * (bitsPerSample / 8));
                writer.Write((short)(channels * (bitsPerSample / 8)));
                writer.Write(bitsPerSample);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataLength);
                for (int frame = 0; frame < frames; frame++)
                {
                    short sample = (short)(Math.Sin(frame * 2.0 * Math.PI * 440.0 / sampleRate) * 8000.0);
                    writer.Write(sample);
                    writer.Write(sample);
                }
            }
        }
    }
}
