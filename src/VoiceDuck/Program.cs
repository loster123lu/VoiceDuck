using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace VoiceDuck
{
    internal static class Program
    {
        private static Mutex _singleInstance;

        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            _singleInstance = new Mutex(true, "Local\\VoiceDuck-8D0260FB-A64D-42B6-A08A-BB702BC4F97D", out created);
            if (!created)
            {
                MessageBox.Show("VoiceDuck 已经在运行，请查看系统托盘。", "VoiceDuck", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppSettings settings = SettingsStore.Load();
            bool startHidden = args.Any(delegate(string arg)
            {
                return String.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase);
            });

            MainForm form = null;
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs eventArgs)
            {
                MessageBox.Show(
                    "VoiceDuck 遇到错误，退出时会恢复由它调整的音量。\n\n" + eventArgs.Exception.Message,
                    "VoiceDuck",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                if (form != null) form.Shutdown();
            };

            try
            {
                form = new MainForm(settings, startHidden);
                Application.Run(form);
            }
            finally
            {
                if (form != null) form.Dispose();
                if (_singleInstance != null)
                {
                    try { _singleInstance.ReleaseMutex(); } catch { }
                    _singleInstance.Dispose();
                }
            }
        }
    }
}
