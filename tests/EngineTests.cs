using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Trazo
{
    internal static class EngineTests
    {
        static int assertions;
        static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool condition,string label){assertions++;if(!condition)throw new Exception(label);}
        static void Set(object o,string field,object value){o.GetType().GetField(field,Flags).SetValue(o,value);}
        static object Call(object o,string method,params object[] args){return o.GetType().GetMethod(method,Flags).Invoke(o,args);}
        static MouseEventArgs Mouse(int x,int y){return new MouseEventArgs(MouseButtons.Left,1,x,y,0);}
        [STAThread] static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                using(Bitmap fixture=new Bitmap(640,480,PixelFormat.Format32bppArgb))
                {
                    using(Graphics g=Graphics.FromImage(fixture)){g.Clear(Color.White);using(Brush b=new SolidBrush(Color.RoyalBlue))g.FillRectangle(b,110,110,50,50);}
                    using(OverlayForm overlay=new OverlayForm(fixture,new Rectangle(-640,-100,640,480)))
                    {
                        Call(overlay,"OnMouseDown",Mouse(100,100));Call(overlay,"OnMouseMove",Mouse(400,300));Call(overlay,"OnMouseUp",Mouse(400,300));
                        using(Bitmap crop=overlay.RenderSelection())
                        {Check(crop.Width==300&&crop.Height==200,"Crop dimensions physical pixels");Check(crop.GetPixel(15,15).ToArgb()==Color.RoyalBlue.ToArgb(),"Crop uses correct source offset");Check(crop.GetPixel(0,0).ToArgb()==Color.White.ToArgb(),"No selection border in exported image");}
                        Panel bar=(Panel)overlay.GetType().GetField("drawbar",Flags).GetValue(overlay);
                        Check(bar.Width>350&&bar.Width<550,"Compact toolbar width");Check(bar.Controls[0].Left==2,"No empty leading toolbar panel");
                        Annotation arrow=new Annotation();arrow.Tool=DrawTool.Arrow;arrow.Color=Color.Red;arrow.Width=4;arrow.Start=new Point(180,180);arrow.End=new Point(320,220);
                        Call(overlay,"AddAnnotation",arrow);
                        using(Bitmap crop=overlay.RenderSelection()){Color c=crop.GetPixel(140,97);Check(c.R>c.B,"Arrow is flattened into output");}
                        Call(overlay,"Undo");using(Bitmap crop=overlay.RenderSelection())Check(crop.GetPixel(140,97).ToArgb()==Color.White.ToArgb(),"Undo restores pixels");
                        Call(overlay,"Redo");using(Bitmap crop=overlay.RenderSelection()){Color c=crop.GetPixel(140,97);Check(c.R>c.B,"Redo restores annotations");}
                        Call(overlay,"ClearAnnotations");Call(overlay,"Undo");
                        Annotation rect=new Annotation();rect.Tool=DrawTool.Rect;rect.Start=new Point(1,1);rect.End=new Point(70,70);rect.Color=Color.Magenta;rect.Width=10;Call(overlay,"AddAnnotation",rect);
                        using(Bitmap scene=overlay.RenderScene())Check(scene.GetPixel(1,30).ToArgb()==Color.White.ToArgb(),"Annotations cannot leak outside selection");
                        Set(overlay,"tool",DrawTool.Select);
                        Call(overlay,"OnMouseDown",Mouse(400,300));Call(overlay,"OnMouseMove",Mouse(480,350));Call(overlay,"OnMouseUp",Mouse(480,350));
                        using(Bitmap crop=overlay.RenderSelection())Check(crop.Width==380&&crop.Height==250,"Resize bottom right");
                        Call(overlay,"ResetSelection");
                        Call(overlay,"OnMouseDown",Mouse(500,400));Call(overlay,"OnMouseMove",Mouse(200,200));Call(overlay,"OnMouseUp",Mouse(200,200));
                        using(Bitmap crop=overlay.RenderSelection())Check(crop.Width==300&&crop.Height==200,"Reverse direction drag");
                        Call(overlay,"FullScreen");using(Bitmap crop=overlay.RenderSelection())Check(crop.Size==fixture.Size,"Full desktop selection");
                    }
                    using(Bitmap pixels=Annotation.Pixelate(fixture,12))Check(pixels.GetPixel(111,111)==pixels.GetPixel(118,118),"Mosaic shares block pixels");
                    using(Bitmap blur=Annotation.Blur(fixture,5)){Check(blur.Size==fixture.Size,"Blur dimensions");Check(blur.GetPixel(110,110).ToArgb()!=fixture.GetPixel(110,110).ToArgb(),"Blur changes edge pixels");Check(blur.GetPixel(300,300).A==255,"Blur opaque output");}
                }
                assertions+=FeatureTests.Run();
                assertions+=EditorIntegrationTests.Run();
                assertions+=ShortcutTests.Run();
                Console.WriteLine("PASS: "+assertions+" checks (capture, annotations, editing, windows, projects and effects)");return 0;
            }
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
    }
}
