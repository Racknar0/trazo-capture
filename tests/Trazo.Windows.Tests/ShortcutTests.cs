using System;
using System.IO;
using System.Windows.Forms;

namespace Trazo
{
    internal static class ShortcutTests
    {
        private static int assertions;
        private static void Check(bool condition,string label)
        {
            assertions++;
            if(!condition)throw new Exception("Shortcut test: "+label);
        }

        internal static int Run()
        {
            assertions=0;
            TestDefaults();
            TestCustomKeys();
            TestCloneAndValidation();
            TestPersistence();
            return assertions;
        }

        private static void TestDefaults()
        {
            ShortcutSettings defaults=ShortcutSettings.CreateDefault();
            Check(defaults.Shortcuts.Count==2,"two default shortcuts");
            Check(defaults.Shortcuts.Contains(Keys.Control|Keys.Q),"Ctrl+Q enabled by default");
            Check(defaults.Shortcuts.Contains(Keys.PrintScreen),"Print Screen enabled by default");
            Check(defaults.Matches((int)Keys.Q,Keys.Control),"default Ctrl+Q matches");
            Check(!defaults.Matches((int)Keys.Q,Keys.None),"Q alone does not match Ctrl+Q");
            Check(!defaults.Matches((int)Keys.Q,Keys.Control|Keys.Shift),"extra Shift prevents Ctrl+Q match");
            Check(defaults.Matches((int)Keys.PrintScreen,Keys.None),"bare Print Screen matches");
            Check(!defaults.Matches((int)Keys.PrintScreen,Keys.Alt),"Alt+Print Screen passes through");
            Check(!defaults.Matches((int)Keys.W,Keys.Control),"Ctrl+W does not trigger capture");
        }

        private static void TestCustomKeys()
        {
            ShortcutSettings custom=ShortcutSettings.CreateDefault();custom.Shortcuts.Clear();
            custom.Shortcuts.Add(Keys.H);
            custom.Shortcuts.Add(Keys.Control|Keys.Alt|Keys.Shift|Keys.P);
            custom.Shortcuts.Add(Keys.F12);
            Check(custom.Matches((int)Keys.H,Keys.None),"arbitrary plain H matches");
            Check(!custom.Matches((int)Keys.H,Keys.Control),"plain key rejects added modifier");
            Check(custom.Matches((int)Keys.P,Keys.Control|Keys.Alt|Keys.Shift),"custom Ctrl+Alt+Shift+P matches");
            Check(!custom.Matches((int)Keys.P,Keys.Control|Keys.Alt),"custom combination needs every modifier");
            Check(custom.Matches((int)Keys.F12,Keys.None),"function key matches");
            Check(!custom.Matches((int)Keys.Q,Keys.Control),"removed default combination is disabled");
            Check(!custom.Matches((int)Keys.PrintScreen,Keys.None),"removed Print Screen is disabled");
            custom.Shortcuts.Clear();
            Check(!custom.Matches((int)Keys.H,Keys.None),"empty list disables previous plain key");
            Check(!custom.Matches((int)Keys.PrintScreen,Keys.None),"empty list disables capture shortcuts");
            custom.Shortcuts.Add(ShortcutSettings.WindowsModifier|Keys.H);
            Check(custom.Matches((int)Keys.H,ShortcutSettings.WindowsModifier),"Windows modifier combination matches");
            Check(!custom.Matches((int)Keys.H,Keys.None),"Windows modifier is required exactly");
        }

        private static void TestCloneAndValidation()
        {
            ShortcutSettings original=ShortcutSettings.CreateDefault();ShortcutSettings copy=original.Clone();
            Check(!Object.ReferenceEquals(original,copy),"settings clone has own instance");
            Check(!Object.ReferenceEquals(original.Shortcuts,copy.Shortcuts),"settings clone has own list");
            copy.Shortcuts.Clear();
            Check(original.Shortcuts.Count==2,"editing clone cannot change active shortcuts");
            Check(!ShortcutSettings.IsValid(Keys.None),"None is not a shortcut");
            Check(!ShortcutSettings.IsValid(Keys.Control|Keys.Alt|Keys.Shift),"modifier flags alone are invalid");
            Check(!ShortcutSettings.IsValid(Keys.ControlKey),"modifier key alone is invalid");
            Check(ShortcutSettings.IsValid(Keys.H),"plain letter is valid");
            Check(ShortcutSettings.IsValid(Keys.F12),"function key is valid");
            Check(ShortcutSettings.IsValid(Keys.Escape),"plain Escape is a configurable key");
            Check(!ShortcutSettings.IsValid(Keys.LButton),"mouse button is not a keyboard shortcut");
            Check(!ShortcutSettings.IsValid((Keys)0x00400000|Keys.H),"unknown modifier bits are rejected");
            Check(ShortcutSettings.IsValid(Keys.Control|Keys.Alt|Keys.Shift|Keys.P),"arbitrary modified letter is valid");
            Check(!String.IsNullOrWhiteSpace(ShortcutSettings.Format(Keys.PrintScreen)),"Print Screen has a readable name");
            Check(ShortcutSettings.Format(Keys.Control|Keys.Q).IndexOf("Q",StringComparison.OrdinalIgnoreCase)>=0,"formatted shortcut includes key");
        }

        private static void TestPersistence()
        {
            string prefix=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"shortcut_"+Guid.NewGuid().ToString("N"));
            string path=prefix+".xml",invalid=prefix+"_invalid.xml",empty=prefix+"_empty.xml",missing=prefix+"_missing.xml";
            try
            {
                ShortcutSettings custom=ShortcutSettings.CreateDefault();custom.Shortcuts.Clear();
                custom.Shortcuts.Add(Keys.H);custom.Shortcuts.Add(Keys.Control|Keys.Alt|Keys.Shift|Keys.P);custom.Save(path);
                ShortcutSettings restored=ShortcutSettings.Load(path);
                Check(restored.Shortcuts.Count==2,"custom shortcut count roundtrips");
                Check(restored.Shortcuts.Contains(Keys.H),"plain key roundtrips");
                Check(restored.Shortcuts.Contains(Keys.Control|Keys.Alt|Keys.Shift|Keys.P),"all modifiers roundtrip");
                Check(restored.Matches((int)Keys.H,Keys.None),"restored plain shortcut is active");
                Check(!restored.Matches((int)Keys.Q,Keys.Control),"persistence does not reenable removed defaults");
                ShortcutSettings disabled=ShortcutSettings.CreateDefault();disabled.Shortcuts.Clear();disabled.Save(empty);
                Check(ShortcutSettings.Load(empty).Shortcuts.Count==0,"explicit empty list stays disabled after restart");
                File.WriteAllText(invalid,"<Shortcuts><Shortcut");
                ShortcutSettings malformed=ShortcutSettings.Load(invalid);
                Check(malformed.Matches((int)Keys.Q,Keys.Control)&&malformed.Matches((int)Keys.PrintScreen,Keys.None),"malformed settings fall back to defaults");
                ShortcutSettings absent=ShortcutSettings.Load(missing);
                Check(absent.Shortcuts.Count==2,"missing file uses defaults without writing app data");
                File.WriteAllText(invalid,"<TrazoSettings version=\"1\"><Shortcut keys=\"72\"/><Shortcut keys=\"72\"/></TrazoSettings>");
                Check(ShortcutSettings.Load(invalid).Shortcuts.Count==2&&ShortcutSettings.Load(invalid).Matches((int)Keys.Q,Keys.Control),"duplicate settings fall back to defaults");
                File.WriteAllText(invalid,"<!DOCTYPE TrazoSettings [<!ENTITY x SYSTEM 'file:///not-a-real-path'>]><TrazoSettings version=\"1\"/>");
                Check(ShortcutSettings.Load(invalid).Matches((int)Keys.Q,Keys.Control),"DTD is rejected without resolving entities");
                custom.Shortcuts.Add(Keys.H);bool duplicateRejected=false;
                try{custom.Save(path);}catch(InvalidDataException){duplicateRejected=true;}
                Check(duplicateRejected,"saving duplicate shortcuts is rejected");
                Check(ShortcutSettings.Load(path).Shortcuts.Count==2,"failed validation preserves existing settings file");
                ShortcutSettings tooMany=new ShortcutSettings();for(int i=0;i<ShortcutSettings.MaxShortcuts+1;i++)tooMany.Shortcuts.Add((Keys)((int)Keys.F1+i));
                bool limitRejected=false;try{tooMany.Save(path);}catch(InvalidDataException){limitRejected=true;}
                Check(limitRejected,"shortcut list size limit is enforced before save");
            }
            finally
            {
                foreach(string file in new string[]{path,invalid,empty,missing})if(File.Exists(file))File.Delete(file);
            }
        }
    }
}
