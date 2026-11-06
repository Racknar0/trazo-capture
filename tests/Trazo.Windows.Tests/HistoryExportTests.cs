using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace Trazo
{
    internal static class HistoryExportTests
    {
        static int assertions;
        static void Check(bool condition,string message){assertions++;if(!condition)throw new Exception(message);}
        internal static int Run()
        {
            assertions=0;
            string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"history_export_"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                CaptureHistoryStore store=new CaptureHistoryStore(directory);
                using(Bitmap source=new Bitmap(100,80))
                {
                    using(Graphics g=Graphics.FromImage(source))g.Clear(Color.White);
                    using(OverlayForm overlay=new OverlayForm(source,new Rectangle(0,0,100,80)))
                    {
                        Check(store.Load().Count==0,"Opening capture does not add history");
                        BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                        overlay.GetType().GetField("selection",flags).SetValue(overlay,new Rectangle(10,10,60,40));
                        overlay.GetType().GetField("ready",flags).SetValue(overlay,true);
                        Annotation mark=new Annotation();mark.Tool=DrawTool.Rect;mark.Color=Color.Red;mark.Width=3;mark.Start=new Point(20,20);mark.End=new Point(50,40);
                        overlay.GetType().GetMethod("AddAnnotation",flags).Invoke(overlay,new object[]{mark});
                        overlay.ImageExported+=delegate(Bitmap image){store.Add(image,30);};
                        bool laterSubscriber=false;
                        overlay.ImageExported+=delegate(Bitmap image){throw new IOException("Simulated subscriber failure");};
                        overlay.ImageExported+=delegate(Bitmap image){laterSubscriber=true;};
                        using(Bitmap rendered=overlay.RenderSelection())overlay.NotifyImageExported(rendered);
                        Check(store.Load().Count==1,"Finished capture stores one history item");
                        Check(laterSubscriber,"History notification isolates subscriber failures");
                        CaptureRecord record=store.Load()[0];Check(record.Width==60&&record.Height==40,"History stores cropped image dimensions");
                        using(Bitmap saved=new Bitmap(record.FullPath)){Check(saved.GetPixel(10,15).R>200&&saved.GetPixel(10,15).G<150,"History preserves flattened annotations");Check(saved.GetPixel(0,0).ToArgb()==Color.White.ToArgb(),"History excludes overlay mask and handles");}
                        overlay.DefaultImageFormat="JPEG";overlay.DefaultSaveDirectory=directory;
                        Check(overlay.DefaultImageFormat=="JPEG"&&overlay.DefaultSaveDirectory==directory,"Capture export accepts saved preferences");
                    }
                    using(OverlayForm cancelled=new OverlayForm(source,new Rectangle(0,0,100,80)))
                    {cancelled.ImageExported+=delegate(Bitmap image){store.Add(image,30);};}
                    Check(store.Load().Count==1,"Cancelled capture does not add history");
                }
                store.Clear();Check(store.Load().Count==0,"History can be cleared after export");
            }
            finally
            {
                foreach(string file in Directory.GetFiles(directory))File.Delete(file);
                Directory.Delete(directory);
            }
            return assertions;
        }
    }
}
