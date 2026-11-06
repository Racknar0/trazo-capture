using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Trazo
{
    internal static class HistorySettingsTests
    {
        private static int assertions;
        private static void Check(bool condition,string label)
        {assertions++;if(!condition)throw new Exception("History/settings test: "+label);}
        private static void Throws(Action action,string label)
        {
            bool threw=false;try{action();}catch(ArgumentException){threw=true;}Check(threw,label);
        }
        internal static int Run()
        {
            assertions=0;
            string parent=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            string root=Path.GetFullPath(Path.Combine(parent,"history_settings_"+Guid.NewGuid().ToString("N")));
            if(!root.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected test directory.");
            Directory.CreateDirectory(root);
            try{TestSettings(root);TestHistory(root);TestStartupCommand();}
            finally
            {
                // Solo se retiran los archivos creados dentro de la carpeta exclusiva de esta prueba.
                foreach(string file in Directory.GetFiles(root,"*",SearchOption.AllDirectories))
                {
                    string path=Path.GetFullPath(file);if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected cleanup path.");
                    File.Delete(path);
                }
                foreach(string child in Directory.GetDirectories(root))Directory.Delete(child,false);
                Directory.Delete(root,false);
            }
            return assertions;
        }
        private static void TestSettings(string root)
        {
            AppSettings defaults=new AppSettings();
            Check(defaults.HistoryEnabled,"history enabled by default");Check(defaults.HistoryLimit==30,"30 captures default");Check(defaults.CloseBehavior==CloseBehavior.Ask,"close action asks by default");
            Check(!defaults.StartWithWindows,"portable first run does not alter startup");Check(defaults.HideMainDuringCapture,"main hidden during capture by default");Check(defaults.StartMinimized,"startup can begin in tray");Check(defaults.ImageFormat=="PNG","PNG default export");Check(Path.IsPathRooted(defaults.SaveDirectory),"default destination absolute");
            Check(AppSettings.IsValidHistoryLimit(1)&&AppSettings.IsValidHistoryLimit(200),"inclusive 1..200 limits");Check(!AppSettings.IsValidHistoryLimit(0)&&!AppSettings.IsValidHistoryLimit(201),"invalid history limits rejected");
            Throws(delegate{AppSettings.ValidateHistoryLimit(0);},"zero limit throws");Throws(delegate{AppSettings.ValidateHistoryLimit(201);},"over200 throws");
            AppSettings copy=defaults.Clone();copy.HistoryLimit=200;copy.ImageFormat="JPEG";copy.CloseBehavior=CloseBehavior.Tray;copy.StartWithWindows=true;copy.StartMinimized=false;copy.HideMainDuringCapture=false;copy.HistoryEnabled=false;copy.SaveDirectory=Path.Combine(root,"Exports");
            Check(defaults.HistoryLimit==30&&defaults.ImageFormat=="PNG","clone independent preferences");
            string path=Path.Combine(root,"preferences.xml");copy.Save(path);AppSettings loaded=AppSettings.Load(path);
            Check(loaded.HistoryLimit==200&&!loaded.HistoryEnabled,"retention and enabled persisted");Check(loaded.ImageFormat=="JPEG"&&loaded.SaveDirectory==copy.SaveDirectory,"export options persisted");Check(loaded.StartWithWindows&&!loaded.StartMinimized&&!loaded.HideMainDuringCapture,"startup and capture options persisted");Check(loaded.CloseBehavior==CloseBehavior.Tray,"close behavior persisted");
            copy.HistoryLimit=1;copy.CloseBehavior=CloseBehavior.Exit;copy.Save(path);loaded=AppSettings.Load(path);Check(loaded.HistoryLimit==1&&loaded.CloseBehavior==CloseBehavior.Exit,"atomic existing preferences overwrite");
            copy.HistoryLimit=201;Throws(delegate{copy.Save(path);},"invalid preferences not saved");Check(AppSettings.Load(path).HistoryLimit==1,"failed save preserves old preferences");
            string missing=Path.Combine(root,"missing.xml");Check(AppSettings.Load(missing).HistoryLimit==30&&!File.Exists(missing),"missing preferences defaults without writing");
            string malformed=Path.Combine(root,"malformed.xml");File.WriteAllText(malformed,"");Check(AppSettings.Load(malformed).HistoryLimit==30,"empty preferences fallback");
            File.WriteAllText(malformed,"<TrazoPreferences version=\"1\" historyLimit=\"NaN\"/>");Check(AppSettings.Load(malformed).HistoryLimit==30,"invalid integer fallback");
            File.WriteAllText(malformed,"<TrazoPreferences version=\"1\" historyLimit=\"201\"/>");Check(AppSettings.Load(malformed).HistoryLimit==30,"out of range preferences fallback");
            File.WriteAllText(malformed,"<!DOCTYPE x [<!ENTITY x SYSTEM \"file:///nonexistent\">]><TrazoPreferences version=\"1\"/>");Check(AppSettings.Load(malformed).HistoryLimit==30,"DTD disabled");
            File.WriteAllText(malformed,"<TrazoPreferences version=\"1\" imageFormat=\"MP4\"/>");Check(AppSettings.Load(malformed).ImageFormat=="PNG","unsupported image format fallback");
            File.WriteAllText(malformed,"<TrazoPreferences version=\"1\"/>");Check(AppSettings.Load(malformed).HistoryLimit==30,"missing optional fields migrate to defaults");
        }
        private static void TestHistory(string root)
        {
            string directory=Path.Combine(root,"Captures"),outside=Path.Combine(root,"outside.png");CaptureHistoryStore store=new CaptureHistoryStore(directory);
            Check(store.Load().Count==0&&!Directory.Exists(directory),"empty history does not create folder");
            using(Bitmap image=new Bitmap(40,25))
            {
                using(Graphics graphics=Graphics.FromImage(image))graphics.Clear(Color.CornflowerBlue);
                image.Save(outside,ImageFormat.Png);Directory.CreateDirectory(directory);string foreign=Path.Combine(directory,"personal.png");image.Save(foreign,ImageFormat.Png);
                CaptureRecord first=store.Add(image,2),second=store.Add(image,2),third=store.Add(image,2);
                List<CaptureRecord> records=store.Load();Check(records.Count==2,"adding third capture prunes to limit2");Check(!File.Exists(first.FullPath)&&File.Exists(second.FullPath)&&File.Exists(third.FullPath),"only oldest owned capture pruned");Check(records[0].Id==third.Id&&records[1].Id==second.Id,"history newest first");
                Check(records[0].Width==40&&records[0].Height==25,"PNG dimensions from header");Check(records[0].CreatedUtc.Kind==DateTimeKind.Utc&&Math.Abs((records[0].CreatedUtc-DateTime.UtcNow).TotalSeconds)<10,"UTC timestamp preserved");Check(File.Exists(foreign),"retention preserves unrelated PNG");
                using(Bitmap saved=new Bitmap(third.FullPath))Check(saved.GetPixel(10,10).ToArgb()==Color.CornflowerBlue.ToArgb(),"history preserves capture pixels");
                Check(!store.Delete(new CaptureRecord{FullPath=outside}),"outside path cannot be deleted");Check(File.Exists(outside),"outside image preserved");Check(!store.Delete(new CaptureRecord{FullPath=foreign}),"foreign image cannot be deleted");Check(File.Exists(foreign),"foreign file survives delete request");
                string corrupt=Path.Combine(directory,"TrazoCapture_20261106T1200000000000Z_"+Guid.NewGuid().ToString("N")+".png");File.WriteAllText(corrupt,"broken PNG");Check(store.Load().Count==2,"corrupt PNG skipped");
                string invalidName=Path.Combine(directory,"TrazoCapture_bad.png");image.Save(invalidName,ImageFormat.Png);Check(store.Load().Count==2,"invalid owned-looking filename skipped");
                Check(store.Delete(third),"owned capture deleted");Check(store.Load().Count==1&&!File.Exists(third.FullPath),"delete reflected in next load");Check(!store.Delete(third),"missing capture delete safely false");
                store.Add(image,200);store.Add(image,200);Check(store.Load().Count==3,"limit200 accepted");store.Prune(1);Check(store.Load().Count==1,"reducing retention prunes oldest immediately");
                Throws(delegate{store.Prune(201);},"prune above200 rejected");Throws(delegate{store.Add(image,0);},"add zero retention rejected");Check(store.Load().Count==1,"invalid limit leaves history untouched");
                store.Clear();Check(store.Load().Count==0,"clear removes valid owned history");Check(File.Exists(foreign)&&File.Exists(outside)&&File.Exists(invalidName),"clear preserves foreign paths and invalid names");
            }
        }
        private static void TestStartupCommand()
        {
            Check(StartupRegistration.BuildCommand(@"C:\Users\A User\Programs\Trazo\Trazo.exe")==@"""C:\Users\A User\Programs\Trazo\Trazo.exe"" --background","startup quotes executable path and starts background");
            Check(StartupRegistration.BuildCommand(@"C:\Users\A User\Programs\Trazo\Trazo.exe",false)==@"""C:\Users\A User\Programs\Trazo\Trazo.exe""","normal startup omits background argument");
            Throws(delegate{StartupRegistration.BuildCommand("bad\"path");},"startup command refuses quote injection");
        }
    }
}
