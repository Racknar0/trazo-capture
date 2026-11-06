using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;
namespace Trazo
{
    internal static class RenderPreview
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        [STAThread] static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            using(Bitmap fixture=new Bitmap(1000,720))
            {
                using(Graphics g=Graphics.FromImage(fixture))
                using(Font title=new Font("Segoe UI",25,FontStyle.Bold))
                using(Font body=new Font("Segoe UI",13))
                {
                    g.Clear(Color.FromArgb(240,244,248));g.FillRectangle(Brushes.White,0,0,1000,68);g.DrawString("Mi espacio de trabajo",title,Brushes.SlateGray,45,14);
                    g.FillRectangle(Brushes.White,95,110,810,370);g.DrawString("Revisión del diseño",title,Brushes.DarkSlateGray,135,145);g.DrawString("Una captura, las marcas que necesitas y listo.",body,Brushes.SlateGray,138,205);
                    g.FillRectangle(Brushes.AliceBlue,136,260,448,140);g.DrawString("Vista previa",title,Brushes.SteelBlue,160,280);g.DrawString("Datos de ejemplo · Sin contenido del escritorio",body,Brushes.SlateGray,160,340);
                    g.FillRectangle(Brushes.DeepSkyBlue,650,285,190,50);g.DrawString("Continuar",body,Brushes.White,697,298);
                }
                using(OverlayForm editor=new OverlayForm(fixture,new Rectangle(0,0,1000,720)))
                {
                    editor.GetType().GetField("selection",Flags).SetValue(editor,new Rectangle(95,110,810,370));editor.GetType().GetField("ready",Flags).SetValue(editor,true);
                    Annotation rect=new Annotation();rect.Tool=DrawTool.Rect;rect.Start=new Point(636,271);rect.End=new Point(856,351);rect.Width=3;rect.Color=Color.Tomato;
                    editor.GetType().GetMethod("AddAnnotation",Flags).Invoke(editor,new object[]{rect});
                    Annotation arrow=new Annotation();arrow.Tool=DrawTool.Arrow;arrow.Start=new Point(535,421);arrow.End=new Point(648,338);arrow.Width=3;arrow.Color=Color.Tomato;
                    editor.GetType().GetMethod("AddAnnotation",Flags).Invoke(editor,new object[]{arrow});
                    editor.GetType().GetMethod("SetTool",Flags).Invoke(editor,new object[]{DrawTool.Select});
                    using(Bitmap preview=new Bitmap(1000,720))
                    using(Graphics g=Graphics.FromImage(preview))
                    {
                        editor.GetType().GetMethod("OnPaint",Flags).Invoke(editor,new object[]{new PaintEventArgs(g,new Rectangle(0,0,1000,720))});
                        foreach(string name in new string[]{"drawbar","operbar","config"})
                        {
                            Panel panel=(Panel)editor.GetType().GetField(name,Flags).GetValue(editor);
                            using(Bitmap controls=new Bitmap(panel.Width,panel.Height))
                            {
                                panel.DrawToBitmap(controls,new Rectangle(Point.Empty,panel.Size));
                                using(Graphics cg=Graphics.FromImage(controls))foreach(Control child in panel.Controls)if(child.Width>0&&child.Height>0)
                                {using(Bitmap item=new Bitmap(child.Width,child.Height)){child.DrawToBitmap(item,new Rectangle(Point.Empty,child.Size));cg.DrawImageUnscaled(item,child.Location);}}
                                g.DrawImageUnscaled(controls,panel.Location);
                            }
                        }
                        preview.Save(args[0],ImageFormat.Png);
                    }
                }
            }
        }
    }
}
