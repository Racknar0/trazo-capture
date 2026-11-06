using System;
using System.Threading;
using System.Windows.Forms;

namespace Trazo
{
    internal static class Program
    {
        internal const string CaptureEventName = "Local\\Trazo.ScreenCapture.Request.v1";

        [STAThread]
        private static void Main(string[] args)
        {
            Native.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool firstInstance;
            using (Mutex mutex = new Mutex(true, "Local\\Trazo.ScreenCapture.v1", out firstInstance))
            {
                if (!firstInstance)
                {
                    try
                    {
                        using (EventWaitHandle request = EventWaitHandle.OpenExisting(CaptureEventName))
                            request.Set();
                    }
                    catch (WaitHandleCannotBeOpenedException)
                    {
                        MessageBox.Show("Trazo se está iniciando. Intenta abrirlo de nuevo en unos segundos.", "Trazo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }
                try
                {
                    using (EventWaitHandle captureRequest = new EventWaitHandle(false, EventResetMode.AutoReset, CaptureEventName))
                    using (MainForm main = new MainForm(captureRequest))
                    {
                        bool capture = Array.IndexOf(args, "--capture") >= 0;
                        bool background = Array.IndexOf(args, "--background") >= 0;
                        if (background && !capture) main.Shown += delegate { main.Hide(); };
                        if (capture) main.Shown += delegate { main.BeginInvoke(new Action(delegate { main.BeginCapture(false); })); };
                        Application.Run(main);
                    }
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
