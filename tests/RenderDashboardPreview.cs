using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
namespace Trazo
{
    internal static class RenderDashboardPreview
    {
        [STAThread]private static void Main(string[] args)
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            string output=Path.GetFullPath(args.Length>0?args[0]:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"dashboard-previews"));Directory.CreateDirectory(output);
            string empty=Path.Combine(output,"fixture-empty"),filled=Path.Combine(output,"fixture-history");Directory.CreateDirectory(empty);Directory.CreateDirectory(filled);
            AppSettings settings=new AppSettings();settings.SaveDirectory=@"C:\Capturas\Trazo";
            using(EventWaitHandle request=new EventWaitHandle(false,EventResetMode.AutoReset))
            {
                using(MainForm dashboard=new MainForm(request,settings,empty,true)){Render(dashboard,Path.Combine(output,"dashboard-empty.png"));}
                CaptureHistoryStore store=new CaptureHistoryStore(filled);
                if(store.Load().Count==0)for(int i=0;i<3;i++)using(Bitmap image=Fixture(i))store.Add(image,30);
                using(MainForm dashboard=new MainForm(request,settings,filled,true)){Render(dashboard,Path.Combine(output,"dashboard-history.png"));}
                using(SettingsForm form=new SettingsForm(settings,@"C:\Capturas\Historial",true,null,true))
                {
                    form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-30000,-30000);form.Show();Application.DoEvents();
                    ListBox navigation=(ListBox)typeof(SettingsForm).GetField("navigation",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
                    string[] names={"settings-general.png","settings-captures.png","settings-history.png","settings-shortcuts.png"};
                    for(int i=0;i<names.Length;i++){navigation.SelectedIndex=i;Application.DoEvents();Capture(form,Path.Combine(output,names[i]));}form.Close();
                }
            }
            Console.WriteLine("Vistas generadas en "+output);
        }
        private static void Render(Form form,string path){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-30000,-30000);form.Show();Application.DoEvents();Capture(form,path);form.Close();}
        private static void Capture(Form form,string path){using(Bitmap bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(path,ImageFormat.Png);}}
        private static Bitmap Fixture(int index)
        {
            Bitmap result=new Bitmap(800,450);using(Graphics g=Graphics.FromImage(result))using(Font heading=new Font("Segoe UI",23,FontStyle.Bold))using(Font body=new Font("Segoe UI",13))
            {
                g.Clear(Color.FromArgb(242,246,251));g.FillRectangle(Brushes.White,25,25,750,400);g.FillRectangle(new SolidBrush(Color.FromArgb(0,163,255)),25,25,750,44);
                string[] titles={"Revisión del diseño","Notas del equipo","Ideas para el proyecto"};g.DrawString(titles[index],heading,Brushes.DarkSlateGray,60,101);
                g.DrawString("Captura de ejemplo · Trazo",body,Brushes.SlateGray,62,158);g.FillRectangle(index==1?Brushes.LemonChiffon:Brushes.AliceBlue,60,218,660,104);g.DrawString("Selecciona, marca y comparte.",body,Brushes.SteelBlue,86,247);
                using(Pen pen=new Pen(Color.Tomato,5)){if(index==0){g.DrawRectangle(pen,49,204,683,129);g.DrawLine(pen,614,373,688,320);g.DrawLine(pen,688,320,660,320);g.DrawLine(pen,688,320,681,346);}else if(index==2)g.DrawEllipse(pen,50,201,686,132);}
            }return result;
        }
    }
}
