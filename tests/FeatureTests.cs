using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Trazo
{
    internal static class FeatureTests
    {
        private static int assertions;
        private static void Check(bool condition,string label)
        {
            assertions++;
            if(!condition)throw new Exception("Feature test: "+label);
        }

        internal static int Run()
        {
            assertions=0;
            TestAnnotations();
            TestWindowDetection();
            TestEffects();
            TestProjectRoundtrip();
            return assertions;
        }

        private static void TestAnnotations()
        {
            using(Annotation image=new Annotation())
            {
                image.Tool=DrawTool.Image;image.Start=new Point(10,20);image.End=new Point(13,22);
                image.Patch=new Bitmap(3,2,PixelFormat.Format32bppArgb);
                using(Graphics g=Graphics.FromImage(image.Patch))g.Clear(Color.RoyalBlue);
                image.Points.Add(new Point(11,21));
                using(Annotation copy=image.Clone())
                {
                    Check(copy.Id==image.Id,"history clone preserves annotation identity");
                    Check(!Object.ReferenceEquals(copy.Patch,image.Patch),"clone owns its bitmap");
                    copy.Patch.SetPixel(0,0,Color.Red);
                    Check(image.Patch.GetPixel(0,0).ToArgb()==Color.RoyalBlue.ToArgb(),"changing clone bitmap cannot mutate old history");
                    copy.Points[0]=Point.Empty;
                    Check(image.Points[0]==new Point(11,21),"clone owns its point list");
                }
                using(Annotation duplicate=image.Duplicate())
                {
                    Check(duplicate.Id!=image.Id,"duplicate creates a distinct annotation identity");
                    Check(duplicate.Start==new Point(26,36)&&duplicate.End==new Point(29,38),"duplicate offsets both endpoints");
                    Check(duplicate.Points[0]==new Point(27,37),"duplicate moves stroke points");
                }
                image.ResizeTo(new Rectangle(5,6,30,20));
                Check(image.GetBounds()==new Rectangle(5,6,30,20),"image resize retains requested destination bounds");
                using(Bitmap output=new Bitmap(50,40,PixelFormat.Format32bppArgb))
                {
                    using(Graphics g=Graphics.FromImage(output)){g.Clear(Color.White);image.Draw(g);}
                    Check(output.GetPixel(20,16).ToArgb()==Color.RoyalBlue.ToArgb(),"image is drawn into its resized rectangle");
                    Check(output.GetPixel(40,16).ToArgb()==Color.White.ToArgb(),"resized image leaves exterior unchanged");
                }
                image.End=image.Start;
                Check(image.GetBounds()==new Rectangle(5,6,3,2),"legacy image without destination uses native dimensions");
            }

            using(Annotation line=new Annotation())
            {
                line.Tool=DrawTool.Line;line.Width=2;line.Start=new Point(10,10);line.End=new Point(110,110);
                Check(line.HitTest(new Point(60,60),3),"diagonal line can be hit along its stroke");
                Check(!line.HitTest(new Point(20,100),3),"empty diagonal bounding box does not select a thin line");
            }
            using(Annotation arrow=new Annotation())
            {
                arrow.Tool=DrawTool.Arrow;arrow.Width=3;arrow.Start=new Point(110,100);arrow.End=new Point(10,20);
                arrow.ResizeTo(new Rectangle(30,40,180,120));
                Check(arrow.Start.X>arrow.End.X&&arrow.Start.Y>arrow.End.Y,"resizing preserves a reverse-direction arrow");
                Check(arrow.Width>3,"resizing scales the stroke width");
            }
            using(Annotation text=new Annotation())
            {
                text.Tool=DrawTool.Text;text.Text="Texto corregido";text.FontSize=22;text.Start=new Point(20,30);text.End=text.Start;
                Rectangle before=text.GetBounds();
                Check(before.Width>20&&before.Height>10,"text bounds use font measurements");
                text.MoveBy(-35,17);
                Rectangle after=text.GetBounds();
                Check(after.Location==new Point(before.X-35,before.Y+17)&&after.Size==before.Size,"moving text changes origin without changing size");
                text.ResizeTo(new Rectangle(after.X,after.Y,after.Width*2,after.Height*2));
                Check(text.FontSize>22,"resizing text scales its font");
            }
        }

        private static CaptureWindow Window(Rectangle bounds,params CaptureWindow[] children)
        {return new CaptureWindow {Bounds=bounds,Children=children};}

        private static void TestWindowDetection()
        {
            Rectangle desktop=new Rectangle(-1920,-200,3840,1280);
            CaptureWindow grandchild=Window(new Rectangle(-1440,40,80,80));
            CaptureWindow child=Window(new Rectangle(-1500,0,300,200),grandchild);
            CaptureWindow sibling=Window(new Rectangle(-1490,20,250,170));
            CaptureWindow top=Window(new Rectangle(-1600,-100,1200,900),child,sibling);
            CaptureWindow back=Window(new Rectangle(-1700,-150,1500,1100),Window(new Rectangle(-1500,0,500,500)));
            CaptureWindow[] windows=new CaptureWindow[]{top,back};
            Check(WindowDetection.FindAt(windows,new Point(-1420,60),desktop)==grandchild.Bounds,"deepest visible child is selected with negative screen coordinates");
            Check(WindowDetection.FindAt(windows,new Point(-1300,170),desktop)==child.Bounds,"overlapping child siblings preserve Z order");
            Check(WindowDetection.FindAt(windows,new Point(-1550,-50),desktop)==top.Bounds,"foreground window wins over a background child");
            Check(WindowDetection.FindAt(windows,new Point(-1650,-100),desktop)==back.Bounds,"point outside foreground falls through to background");
            CaptureWindow clipped=Window(new Rectangle(-200,-100,100,100),Window(new Rectangle(-240,-140,150,150)));
            Check(WindowDetection.FindAt(new CaptureWindow[]{clipped},new Point(-150,-50),desktop)==clipped.Bounds,"child bounds are clipped to their parent");
            CaptureWindow tiny=Window(new Rectangle(20,20,120,120),Window(new Rectangle(30,30,8,8)));
            Check(WindowDetection.FindAt(new CaptureWindow[]{tiny},new Point(32,32),desktop)==tiny.Bounds,"tiny child controls do not replace useful window capture");
            Check(WindowDetection.FindAt(windows,new Point(-1420,60),Rectangle.Empty)==Rectangle.Empty,"empty virtual desktop has no capture region");
        }

        private static void TestEffects()
        {
            using(Bitmap source=new Bitmap(3,2,PixelFormat.Format32bppArgb))
            {
                source.SetPixel(0,0,Color.Red);source.SetPixel(1,0,Color.Green);source.SetPixel(2,0,Color.Blue);
                source.SetPixel(0,1,Color.Yellow);source.SetPixel(1,1,Color.Magenta);source.SetPixel(2,1,Color.Cyan);
                using(Bitmap rotated=ImageEffects.Apply(source,new ExportOptions {QuarterTurns=1}))
                {
                    Check(rotated.Size==new Size(2,3),"quarter turn swaps dimensions");
                    Check(rotated.GetPixel(1,0).ToArgb()==Color.Red.ToArgb()&&rotated.GetPixel(0,2).ToArgb()==Color.Cyan.ToArgb(),"quarter turn rotates exact corner pixels clockwise");
                }
                using(Bitmap rotated=ImageEffects.Apply(source,new ExportOptions {QuarterTurns=-1}))
                {
                    Check(rotated.Size==new Size(2,3)&&rotated.GetPixel(0,2).ToArgb()==Color.Red.ToArgb(),"negative turn normalizes counterclockwise");
                }
                using(Bitmap gray=ImageEffects.Apply(source,new ExportOptions {Grayscale=true}))
                {
                    Color red=gray.GetPixel(0,0),blue=gray.GetPixel(2,0);
                    Check(red.R==red.G&&red.G==red.B&&blue.R==blue.G&&blue.G==blue.B,"grayscale produces equal RGB channels");
                    Check(red.R>blue.R,"grayscale applies luminance weights");
                }
                using(Bitmap border=ImageEffects.Apply(source,new ExportOptions {Border=true}))
                {
                    Check(border.Size==new Size(27,26),"border adds twelve pixels on all sides");
                    Check(border.GetPixel(0,0).ToArgb()==Color.White.ToArgb()&&border.GetPixel(12,12).ToArgb()==Color.Red.ToArgb(),"border keeps a white frame and original image pixels");
                }
                using(Bitmap shadow=ImageEffects.Apply(source,new ExportOptions {Shadow=true}))
                {
                    Check(shadow.Size==new Size(35,34),"shadow reserves transparent margin");
                    Check(shadow.GetPixel(0,0).A==0&&shadow.GetPixel(16,16).ToArgb()==Color.Red.ToArgb(),"shadow preserves transparent exterior and source placement");
                    Color falloff=shadow.GetPixel(21,22);
                    Check(falloff.A>0&&falloff.A<255&&falloff.R==0&&falloff.G==0&&falloff.B==0,"shadow draws a translucent dark falloff");
                }
                using(Bitmap result=ImageEffects.Apply(source,new ExportOptions {QuarterTurns=1,Border=true,Shadow=true,Grayscale=true}))
                    Check(result.Size==new Size(58,59),"combined effects compose rotation before border and shadow");
                Check(source.Size==new Size(3,2)&&source.GetPixel(0,0).ToArgb()==Color.Red.ToArgb(),"export effects never mutate the editable source");
            }
        }

        private static void TestProjectRoundtrip()
        {
            string root=Path.Combine(Path.GetDirectoryName(typeof(FeatureTests).Assembly.Location),"FeatureTestsTemp-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string project=Path.Combine(root,"roundtrip.trazo");
            string dtd=Path.Combine(root,"dtd.trazo");
            string version=Path.Combine(root,"version.trazo");
            string malformed=Path.Combine(root,"malformed.trazo");
            try
            {
                using(Bitmap cropped=new Bitmap(80,50,PixelFormat.Format32bppArgb))
                using(Annotation text=new Annotation())
                using(Annotation arrow=new Annotation())
                using(Annotation image=new Annotation())
                {
                    using(Graphics g=Graphics.FromImage(cropped))g.Clear(Color.LightSkyBlue);
                    cropped.SetPixel(0,0,Color.OrangeRed);
                    text.Tool=DrawTool.Text;text.Start=new Point(4,5);text.End=text.Start;text.Text="Texto corregido & <nuevo>";text.FontSize=18;text.Color=Color.DarkRed;
                    arrow.Tool=DrawTool.Arrow;arrow.Start=new Point(70,40);arrow.End=new Point(15,12);arrow.Width=4;arrow.Color=Color.Purple;arrow.Points.Add(new Point(30,20));arrow.Points.Add(new Point(40,28));
                    image.Tool=DrawTool.Image;image.Start=new Point(35,10);image.End=new Point(53,22);image.Patch=new Bitmap(5,4,PixelFormat.Format32bppArgb);
                    using(Graphics g=Graphics.FromImage(image.Patch))g.Clear(Color.LimeGreen);
                    Rectangle bounds=new Rectangle(-300,-200,80,50),selection=new Rectangle(0,0,80,50);
                    ExportOptions effects=new ExportOptions {Grayscale=true,Border=true,Shadow=true,QuarterTurns=3};
                    Annotation[] annotations=new Annotation[]{text,arrow,image};
                    ProjectStore.Save(project,cropped,bounds,selection,annotations,effects);
                    using(ProjectData loaded=ProjectStore.Load(project))
                    {
                        Check(loaded.Screenshot.Size==new Size(80,50)&&loaded.Selection==selection,"project keeps cropped screenshot dimensions and capture area");
                        Check(loaded.VirtualBounds==bounds,"project keeps negative virtual screen origin");
                        Check(!Object.ReferenceEquals(loaded.Screenshot,cropped)&&loaded.Screenshot.GetPixel(0,0).ToArgb()==Color.OrangeRed.ToArgb(),"project screenshot owns lossless decoded pixels");
                        Check(loaded.Annotations.Length==3&&loaded.Annotations[0].Text==text.Text,"project preserves corrected and XML-escaped text in layer order");
                        Check(loaded.Annotations[0].FontSize==18&&loaded.Annotations[0].Color.ToArgb()==Color.DarkRed.ToArgb(),"project preserves editable text styling");
                        Check(loaded.Annotations[1].Id==arrow.Id&&loaded.Annotations[1].Start==arrow.Start&&loaded.Annotations[1].End==arrow.End,"project preserves arrow identity and reverse direction");
                        Check(loaded.Annotations[1].Points.Count==2&&loaded.Annotations[1].Points[1]==new Point(40,28),"project preserves all stroke points");
                        Check(loaded.Annotations[2].GetBounds()==image.GetBounds()&&loaded.Annotations[2].Patch.Size==image.Patch.Size,"project preserves image native pixels and destination size separately");
                        loaded.Annotations[2].Patch.SetPixel(0,0,Color.Black);
                        Check(image.Patch.GetPixel(0,0).ToArgb()==Color.LimeGreen.ToArgb(),"loaded image pixels do not share ownership with saved annotations");
                        Check(loaded.Effects.Grayscale&&loaded.Effects.Border&&loaded.Effects.Shadow&&loaded.Effects.QuarterTurns==3,"project roundtrips every export effect");
                        loaded.Annotations[0].Text="Segunda edición";
                        Check(text.Text=="Texto corregido & <nuevo>","loaded text remains independently editable");
                    }
                    text.Text="Guardado actualizado";
                    ProjectStore.Save(project,cropped,bounds,selection,annotations,effects);
                    using(ProjectData replaced=ProjectStore.Load(project))
                        Check(replaced.Annotations[0].Text=="Guardado actualizado","saving again atomically replaces the existing project");
                }
                File.WriteAllText(dtd,"<!DOCTYPE TrazoProject [<!ENTITY blocked 'blocked'>]><TrazoProject version='1'>&blocked;</TrazoProject>");
                File.WriteAllText(version,"<TrazoProject version='999' />");
                File.WriteAllText(malformed,"<TrazoProject version='1'><Canvas x='bad' /></TrazoProject>");
                ExpectProjectRejected(dtd,"project reader rejects DTDs");
                ExpectProjectRejected(version,"project reader rejects unknown format versions");
                ExpectProjectRejected(malformed,"project reader rejects malformed required canvas data");
            }
            finally
            {
                foreach(string file in new string[]{project,dtd,version,malformed})if(File.Exists(file))File.Delete(file);
                if(Directory.Exists(root))Directory.Delete(root,false);
            }
        }

        private static void ExpectProjectRejected(string path,string label)
        {
            bool rejected=false;
            try{using(ProjectData unexpected=ProjectStore.Load(path)){} }
            catch(System.Xml.XmlException){rejected=true;}
            catch(InvalidDataException){rejected=true;}
            Check(rejected,label);
        }
    }
}
