using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Trazo
{
    internal static class UiSettingsTests
    {
        static int count;
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool condition,string label){count++;if(!condition)throw new Exception(label);}
        static object Get(object target,string name){return target.GetType().GetField(name,Flags).GetValue(target);}
        static object Call(object target,string name,params object[] args){return target.GetType().GetMethod(name,Flags).Invoke(target,args);}
        static Control Find(Control root,string text){foreach(Control c in root.Controls){if(c.Text==text)return c;Control nested=Find(c,text);if(nested!=null)return nested;}return null;}
        static void Offscreen(Form form){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-30000,-30000);form.Show();Application.DoEvents();}
        internal static int Run()
        {
            count=0;string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui_settings_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try
            {
                AppSettings settings=new AppSettings();settings.HistoryLimit=2;settings.SaveDirectory=directory;
                CaptureHistoryStore store=new CaptureHistoryStore(directory);
                using(MainForm main=new MainForm(null,settings,directory,true))
                {
                    Offscreen(main);
                    Control brand=Find(main,"Trazo");Check(brand!=null&&brand.Parent.ClientRectangle.Contains(brand.Bounds),"Dashboard brand fits title bar");
                    Control caption=Find(main,"Capturas de pantalla");Check(caption!=null&&caption.Parent.ClientRectangle.Contains(caption.Bounds),"Dashboard title fits header row");
                    Check(Find(main,"Capturar pantalla")!=null&&Find(main,"Configuración")!=null,"Dashboard exposes capture and settings");
                    FlowLayoutPanel gallery=(FlowLayoutPanel)Get(main,"gallery");Check(gallery.Controls.Count==0,"Empty dashboard starts without items");
                    using(Bitmap source=new Bitmap(90,60))using(OverlayForm overlay=new OverlayForm(source,new Rectangle(0,0,90,60)))
                    {
                        using(Graphics g=Graphics.FromImage(source))g.Clear(Color.DeepSkyBlue);
                        Call(main,"PrepareOverlay",overlay);
                        Check(overlay.DefaultSaveDirectory==directory&&overlay.DefaultImageFormat=="PNG","Dashboard passes output preferences to editor");
                        overlay.NotifyImageExported(source);overlay.NotifyImageExported(source);overlay.NotifyImageExported(source);
                        Check(store.Load().Count==2&&gallery.Controls.Count==2,"Dashboard applies configured retention after exports");
                    }
                    CaptureRecord first=store.Load()[0];Check(store.Delete(first),"Thumbnails do not lock history image files");main.RefreshHistory();Check(gallery.Controls.Count==1,"Dashboard refresh reflects removed history file");
                }
                AppSettings disabled=settings.Clone();disabled.HistoryEnabled=false;
                using(MainForm main=new MainForm(null,disabled,directory,true))using(Bitmap source=new Bitmap(20,20))using(OverlayForm overlay=new OverlayForm(source,new Rectangle(0,0,20,20)))
                {Call(main,"PrepareOverlay",overlay);int before=store.Load().Count;overlay.NotifyImageExported(source);Check(store.Load().Count==before,"Disabled history does not store finished captures");}
                using(SettingsForm form=new SettingsForm(new AppSettings{SaveDirectory=directory},directory,false,null,true))
                {
                    Offscreen(form);
                    Control save=Find(form,"Guardar"),cancel=Find(form,"Cancelar"),close=Find(form,"×");
                    Check(save!=null&&save.Parent.ClientRectangle.Contains(save.Bounds),"Settings Save button stays visible");Check(cancel!=null&&cancel.Parent.ClientRectangle.Contains(cancel.Bounds),"Settings Cancel button stays visible");
                    Check(close!=null&&close.Parent.ClientRectangle.Contains(close.Bounds),"Settings title close button stays visible");
                    NumericUpDown limit=(NumericUpDown)Get(form,"historyLimit");Check(limit.Value==30&&limit.Minimum==1&&limit.Maximum==200,"Settings exposes default30 and maximum200");
                    limit.Value=200;((CheckBox)Get(form,"startup")).Checked=true;((ComboBox)Get(form,"closeBehavior")).SelectedIndex=(int)CloseBehavior.Tray;
                    Call(form,"SaveChanges");Check(form.ResultSettings.HistoryLimit==200&&form.ResultSettings.StartWithWindows&&form.ResultSettings.CloseBehavior==CloseBehavior.Tray,"Settings returns chosen history startup and closing behavior");
                }
                using(ClosePromptForm prompt=new ClosePromptForm())
                {Check(prompt.ResultBehavior==CloseBehavior.Ask&&!prompt.RememberChoice,"Close prompt does not silently remember a decision");Check(Find(prompt,"Dejar en la bandeja")!=null&&Find(prompt,"Cerrar Trazo")!=null&&Find(prompt,"Cancelar")!=null,"Close prompt offers tray exit and cancel");}
            }
            finally{foreach(string path in Directory.GetFiles(directory))File.Delete(path);Directory.Delete(directory);}
            return count;
        }
    }
}
