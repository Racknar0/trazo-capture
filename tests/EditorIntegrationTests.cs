using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Trazo
{
    internal static class EditorIntegrationTests
    {
        static int count;
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool value,string label){count++;if(!value)throw new Exception(label);}
        static object Get(object target,string name){return target.GetType().GetField(name,Flags).GetValue(target);}
        static void Set(object target,string name,object value){target.GetType().GetField(name,Flags).SetValue(target,value);}
        static object Call(object target,string method,params object[] args){return target.GetType().GetMethod(method,Flags).Invoke(target,args);}
        static MouseEventArgs Mouse(int x,int y){return new MouseEventArgs(MouseButtons.Left,1,x,y,0);}
        static Annotation Current(OverlayForm overlay){return (Annotation)overlay.GetType().GetProperty("SelectedAnnotation",Flags).GetValue(overlay,null);}
        static Annotation[] Values(OverlayForm overlay){return ((System.Collections.Generic.List<Annotation[]>)Get(overlay,"history"))[(int)Get(overlay,"historyIndex")];}
        static Annotation Shape(DrawTool tool,Point start,Point end,Color color){Annotation a=new Annotation();a.Tool=tool;a.Start=start;a.End=end;a.Color=color;a.Width=2;return a;}
        internal static int Run()
        {
            count=0;
            using(Bitmap fixture=new Bitmap(800,600,PixelFormat.Format32bppArgb))
            {
                using(Graphics g=Graphics.FromImage(fixture)){g.Clear(Color.White);g.FillRectangle(Brushes.DeepSkyBlue,100,100,200,150);}
                CaptureWindow child=new CaptureWindow{Bounds=new Rectangle(100,110,130,100),Children=new CaptureWindow[0]};
                CaptureWindow window=new CaptureWindow{Bounds=new Rectangle(70,80,350,250),Children=new CaptureWindow[]{child}};
                using(OverlayForm overlay=new OverlayForm(fixture,new Rectangle(0,0,800,600),new CaptureWindow[]{window}))
                {
                    Call(overlay,"OnMouseMove",Mouse(120,140));Call(overlay,"OnMouseDown",Mouse(120,140));Call(overlay,"OnMouseUp",Mouse(120,140));
                    Check((bool)Get(overlay,"ready"),"Window click finalizes selection");Check((Rectangle)Get(overlay,"selection")==child.Bounds,"Window click selects deepest child");
                    Call(overlay,"ResetSelection");Call(overlay,"OnMouseDown",Mouse(10,20));Call(overlay,"OnMouseMove",Mouse(800,600));Call(overlay,"OnMouseUp",Mouse(800,600));
                    Check((Rectangle)Get(overlay,"selection")==new Rectangle(10,20,790,580),"Manual drag includes final screen row/column");
                    Call(overlay,"FullScreen");
                    Annotation original=Shape(DrawTool.Rect,new Point(120,100),new Point(220,180),Color.Red);Call(overlay,"AddAnnotation",original);
                    Call(overlay,"OnMouseDown",Mouse(150,130));Call(overlay,"OnMouseMove",Mouse(190,160));Call(overlay,"OnMouseUp",Mouse(190,160));
                    Check(Current(overlay).Start==new Point(160,130),"Selected object moves by drag");Check(original.Start==new Point(120,100),"Drag preserves prior undo snapshot");
                    Call(overlay,"Undo");Check(Current(overlay).Start==original.Start,"Movement undo restores geometry");Call(overlay,"Redo");
                    Call(overlay,"DuplicateSelected");Check(Values(overlay).Length==2,"Duplicate adds independent object");Check(Current(overlay).Id!=original.Id,"Duplicate changes object identity");
                    Call(overlay,"DeleteSelected");Check(Values(overlay).Length==1,"Delete selected object");Call(overlay,"Undo");Check(Values(overlay).Length==2,"Undo deletion");
                    Annotation selected=Current(overlay);Rectangle before=selected.GetBounds();Point handle=new Point(before.Right,before.Bottom);
                    Call(overlay,"OnMouseDown",Mouse(handle.X,handle.Y));Call(overlay,"OnMouseMove",Mouse(handle.X+50,handle.Y+35));Call(overlay,"OnMouseUp",Mouse(handle.X+50,handle.Y+35));
                    Check(Current(overlay).GetBounds().Width>before.Width+30,"Resize handle expands object");
                    Call(overlay,"ReorderSelected",-1,true);Check(Values(overlay)[0].Id==Current(overlay).Id,"Send selected object to back");Call(overlay,"Undo");Check(Values(overlay)[1].Id==Current(overlay).Id,"Layer reorder undo");
                    Annotation text=Shape(DrawTool.Text,new Point(320,240),new Point(320,240),Color.Black);text.Text="Original";text.FontSize=22;Call(overlay,"AddAnnotation",text);
                    Call(overlay,"OnMouseDoubleClick",Mouse(330,250));TextBox input=(TextBox)Get(overlay,"input");Check(input!=null&&input.Text=="Original","Double click opens existing text");
                    input.Text="Texto corregido";Call(overlay,"CommitText");Check(Current(overlay).Text=="Texto corregido","Commit changes existing text");Check(Values(overlay).Length==3,"Editing text does not duplicate it");Call(overlay,"Undo");Check(Current(overlay).Text=="Original","Text edit undo");Call(overlay,"Redo");
                    Set(overlay,"ink",Color.ForestGreen);Call(overlay,"ApplySelectedStyle",true,false,false);Check(Current(overlay).Color==Color.ForestGreen,"Recolor selected text");
                    using(Bitmap logo=new Bitmap(40,30)){using(Graphics g=Graphics.FromImage(logo))g.Clear(Color.Gold);Call(overlay,"InsertImage",logo);}
                    Check(Current(overlay).Tool==DrawTool.Image&&Current(overlay).Patch!=null,"Image insertion creates editable object");
                    Set(overlay,"selection",new Rectangle(100,100,200,150));Set(overlay,"tool",DrawTool.Select);
                    Annotation hidden=Shape(DrawTool.Rect,new Point(1,1),new Point(70,70),Color.Purple);Call(overlay,"AddAnnotation",hidden);
                    Check(!(bool)Call(overlay,"BeginObjectEdit",new Point(40,40)),"Shaded area cannot select hidden annotation");
                    Annotation partial=new Annotation();partial.Tool=DrawTool.Image;partial.Start=new Point(50,50);partial.End=new Point(250,250);partial.Patch=new Bitmap(200,200);using(Graphics g=Graphics.FromImage(partial.Patch))g.Clear(Color.Orange);Call(overlay,"AddAnnotation",partial);
                    string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"editor_"+Guid.NewGuid().ToString("N")+".trazo");
                    try
                    {
                        overlay.WriteProject(file);
                        using(ProjectData data=ProjectStore.Load(file))
                        {
                            Check(data.Screenshot.Size==new Size(200,150),"Project excludes unselected desktop pixels");Check(data.Selection==new Rectangle(0,0,200,150),"Project crop is normalized");
                            Annotation image=Array.Find(data.Annotations,delegate(Annotation a){return a.Id==partial.Id;});
                            Check(image!=null&&image.Patch.Size==new Size(150,150),"Project excludes off-crop image patch pixels");Check(image.Start==Point.Empty,"Project rebases partial image position");
                            using(OverlayForm reopened=new OverlayForm(data))using(Bitmap render=reopened.RenderSelection())Check(render.Size==data.Selection.Size,"Saved project reopens with correct output size");
                        }
                    }
                    finally{if(File.Exists(file))File.Delete(file);}
                    Set(overlay,"selection",new Rectangle(100,100,300,200));Call(overlay,"SetTool",DrawTool.Rect);
                    Panel config=(Panel)Get(overlay,"config");Check(config.Right<=overlay.ClientSize.Width,"Tool config remains inside screen bounds");
                    Call(overlay,"ResetSelection");Check(Values(overlay).Length==0,"Reset clears editor objects");Check(((System.Collections.Generic.List<Annotation>)Get(overlay,"all")).Count==0,"Reset releases obsolete image history");
                }
                using(ProjectData generic=new ProjectData())
                {
                    generic.Screenshot=(Bitmap)fixture.Clone();generic.VirtualBounds=new Rectangle(-800,-100,800,600);generic.Selection=new Rectangle(100,100,200,150);generic.Annotations=new Annotation[0];generic.Effects=new ExportOptions();
                    using(OverlayForm reopened=new OverlayForm(generic))using(Bitmap render=reopened.RenderSelection())
                    {Check(render.Size==new Size(200,150),"Inner-selection project opens without extra canvas");Check(render.GetPixel(0,0).ToArgb()==Color.DeepSkyBlue.ToArgb(),"Reopened project maps exact selected source pixels");}
                }
            }
            return count;
        }
    }
}
