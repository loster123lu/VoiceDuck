using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace VoiceDuck
{
    internal static class UiRenderTest
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length != 2) throw new ArgumentException("Expected main-window and music-share PNG paths.");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppSettings settings = AppSettings.CreateDefault();
            settings.Enabled = false;
            using (var form = new MainForm(settings, false))
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
                using (var shareForm = new MusicShareForm(settings, service, delegate { }))
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

                    using (var bitmap = new Bitmap(shareForm.Width, shareForm.Height))
                    {
                        shareForm.DrawToBitmap(bitmap, new Rectangle(Point.Empty, shareForm.Size));
                        bitmap.Save(args[1], ImageFormat.Png);
                    }
                    shareForm.PrepareForOwnerShutdown();
                    shareForm.Close();
                    Application.DoEvents();
                }
                form.Shutdown();
                Application.DoEvents();
            }
            Console.WriteLine("UI_RENDERED=" + args[0]);
            Console.WriteLine("MUSIC_SHARE_UI_RENDERED=" + args[1]);
        }
    }
}
