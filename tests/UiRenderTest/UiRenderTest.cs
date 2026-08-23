using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace VoiceDuck
{
    internal static class UiRenderTest
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length != 3)
                throw new ArgumentException("Expected main-window, music-share, and hearing-protection PNG paths.");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppSettings settings = AppSettings.CreateDefault();
            settings.Enabled = false;
            var microphoneSwitcher = new NoOpDefaultMicrophoneSwitcher();
            using (var form = new MainForm(settings, false, microphoneSwitcher))
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-2000, -2000);
                form.Show();
                for (int i = 0; i < 8; i++)
                {
                    Application.DoEvents();
                    Thread.Sleep(75);
                }

                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(args[0], ImageFormat.Png);
                }

                using (var service = new MusicShareAudioEngine())
                using (var shareForm = new MusicShareForm(
                    settings,
                    service,
                    microphoneSwitcher,
                    delegate { }))
                {
                    shareForm.ShowInTaskbar = false;
                    shareForm.StartPosition = FormStartPosition.Manual;
                    shareForm.Location = new Point(-2000, -2000);
                    shareForm.Show();
                    for (int i = 0; i < 8; i++)
                    {
                        Application.DoEvents();
                        Thread.Sleep(75);
                    }

                    MethodInfo updateStopButton = typeof(MusicShareForm).GetMethod(
                        "UpdateShareToggleButtonAppearance",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    FieldInfo stopButtonField = typeof(MusicShareForm).GetField(
                        "_startButton",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (updateStopButton == null || stopButtonField == null)
                        throw new InvalidOperationException("Stop-sharing button state hook was not found.");
                    updateStopButton.Invoke(shareForm, new object[] { true });
                    Button stopButton = stopButtonField.GetValue(shareForm) as Button;
                    if (stopButton == null || !stopButton.Enabled || stopButton.Text != "停止分享" ||
                        stopButton.BackColor.R < 240 ||
                        stopButton.BackColor.G > 70 || stopButton.BackColor.B > 90 ||
                        stopButton.ForeColor != Color.White || !stopButton.Font.Bold)
                        throw new InvalidOperationException("Active stop-sharing button is not visually prominent enough.");

                    using (var bitmap = new Bitmap(shareForm.Width, shareForm.Height))
                    {
                        shareForm.DrawToBitmap(bitmap, new Rectangle(Point.Empty, shareForm.Size));
                        bitmap.Save(args[1], ImageFormat.Png);
                    }
                    shareForm.PrepareForOwnerShutdown();
                    shareForm.Close();
                    Application.DoEvents();
                }

                using (var protectionService = new HearingProtectionService(settings))
                using (var protectionForm = new HearingProtectionForm(settings, protectionService, delegate { }))
                {
                    protectionForm.ShowInTaskbar = false;
                    protectionForm.StartPosition = FormStartPosition.Manual;
                    protectionForm.Location = new Point(-2000, -2000);
                    protectionForm.Show();
                    for (int i = 0; i < 5; i++)
                    {
                        Application.DoEvents();
                        Thread.Sleep(50);
                    }

                    using (var bitmap = new Bitmap(protectionForm.Width, protectionForm.Height))
                    {
                        protectionForm.DrawToBitmap(bitmap, new Rectangle(Point.Empty, protectionForm.Size));
                        bitmap.Save(args[2], ImageFormat.Png);
                    }
                    VerifyPreciseTrackClick(protectionForm);
                    protectionForm.PrepareForOwnerShutdown();
                    protectionForm.Close();
                    Application.DoEvents();
                }
                form.Shutdown();
                Application.DoEvents();
            }
            Console.WriteLine("UI_RENDERED=" + args[0]);
            Console.WriteLine("MUSIC_SHARE_UI_RENDERED=" + args[1]);
            Console.WriteLine("HEARING_PROTECTION_UI_RENDERED=" + args[2]);
        }

        private static void VerifyPreciseTrackClick(HearingProtectionForm protectionForm)
        {
            FieldInfo trackField = typeof(HearingProtectionForm).GetField(
                "_maxVolumeTrack",
                BindingFlags.Instance | BindingFlags.NonPublic);
            TrackBar track = trackField == null ? null : trackField.GetValue(protectionForm) as TrackBar;
            if (track == null) throw new InvalidOperationException("Maximum-volume track bar was not found.");

            NativeRectangle channel = new NativeRectangle();
            SendMessageRectangle(track.Handle, 0x0400 + 26, IntPtr.Zero, ref channel);
            int clickX = channel.Left + (channel.Right - channel.Left) / 4;
            int clickY = track.ClientSize.Height / 2;
            int expected = TrackBarValueMapper.FromPosition(
                track.Minimum,
                track.Maximum,
                track.SmallChange,
                channel.Left,
                channel.Right,
                clickX,
                false);
            int original = track.Value;
            int packedPoint = (clickY << 16) | (clickX & 0xffff);
            SendMessageValue(track.Handle, 0x0201, IntPtr.Zero, new IntPtr(packedPoint));
            Application.DoEvents();
            if (track.Value != expected)
                throw new InvalidOperationException(
                    "Track click mapped to " + track.Value + " instead of " + expected + ".");
            track.Value = original;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessageRectangle(
            IntPtr window,
            int message,
            IntPtr wParam,
            ref NativeRectangle lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessageValue(
            IntPtr window,
            int message,
            IntPtr wParam,
            IntPtr lParam);
    }
}
