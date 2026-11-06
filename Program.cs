using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Trazo
{
    internal static class Program
    {
        internal const string CaptureEventName = "Local\\Trazo.ScreenCapture.Request.v1";
        internal const string ShowEventName="Local\\Trazo.Show.Request.v2";
        internal const string ExitEventName="Local\\Trazo.Exit.Request.v2";
        private static string PendingProjectPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Trazo","pending-project.txt"); } }

        [STAThread]
        private static void Main(string[] args)
        {
            Native.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool capture=Array.IndexOf(args,"--capture")>=0;
            bool exit=Array.IndexOf(args,"--exit")>=0;
            bool background=Array.IndexOf(args,"--background")>=0;
            string openPath=null;int openIndex=Array.IndexOf(args,"--open");
            if(openIndex>=0&&openIndex+1<args.Length)
            {
                try{openPath=Path.GetFullPath(args[openIndex+1]);if(!File.Exists(openPath)||!String.Equals(Path.GetExtension(openPath),".trazo",StringComparison.OrdinalIgnoreCase))throw new IOException("Elige un archivo .trazo existente.");}
                catch(Exception error){MessageBox.Show(error.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
            }
            bool firstInstance;
            using (Mutex mutex = new Mutex(true, "Local\\Trazo.ScreenCapture.v1", out firstInstance))
            {
                if (!firstInstance)
                {
                    try
                    {
                        if(openPath!=null)WritePendingProject(openPath);
                        using (EventWaitHandle request = EventWaitHandle.OpenExisting(exit?ExitEventName:capture?CaptureEventName:ShowEventName))
                            request.Set();
                    }
                    catch (WaitHandleCannotBeOpenedException)
                    {
                        if(!exit)MessageBox.Show("Hay otra versión de Trazo abierta. Ciérrala desde su icono junto al reloj y vuelve a abrir esta versión.", "Trazo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }
                if(exit){mutex.ReleaseMutex();return;}
                try
                {
                    using (EventWaitHandle captureRequest = new EventWaitHandle(false, EventResetMode.AutoReset, CaptureEventName))
                    using(EventWaitHandle showRequest=new EventWaitHandle(false,EventResetMode.AutoReset,ShowEventName))
                    using(EventWaitHandle exitRequest=new EventWaitHandle(false,EventResetMode.AutoReset,ExitEventName))
                    using (MainForm main = new MainForm(captureRequest))
                    using(System.Windows.Forms.Timer requests=new System.Windows.Forms.Timer { Interval=200 })
                    {
                        if (background && !capture) main.Shown += delegate { main.Hide(); };
                        if (capture) main.Shown += delegate { main.BeginInvoke(new Action(delegate { main.BeginCapture(false); })); };
                        if(openPath!=null)main.Shown+=delegate{main.BeginInvoke(new Action(delegate{main.OpenProjectFile(openPath);}));};
                        requests.Tick+=delegate
                        {
                            if(exitRequest.WaitOne(0)){main.RequestExit();return;}
                            if(showRequest.WaitOne(0)){main.ShowFromExternalRequest();string project=TakePendingProject();if(project!=null)main.OpenProjectFile(project);}
                        };
                        requests.Start();
                        Application.Run(main);
                    }
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
        private static void WritePendingProject(string path)
        {
            string file=PendingProjectPath;Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,path,new UTF8Encoding(false));
        }
        private static string TakePendingProject()
        {
            string file=PendingProjectPath;
            try
            {
                if(!File.Exists(file))return null;FileInfo info=new FileInfo(file);if(info.Length>65536){File.Delete(file);return null;}
                string path=File.ReadAllText(file,Encoding.UTF8);File.Delete(file);
                if(path.Length>0&&File.Exists(path)&&String.Equals(Path.GetExtension(path),".trazo",StringComparison.OrdinalIgnoreCase))return Path.GetFullPath(path);
            }
            catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}
            return null;
        }
    }
}
