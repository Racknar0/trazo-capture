using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml;

namespace Trazo
{
    internal sealed class ShortcutSettings
    {
        internal const int MaxShortcuts=12;
        internal readonly List<Keys> Shortcuts=new List<Keys>();
        internal const Keys WindowsModifier=(Keys)0x00080000;
        private const Keys SupportedModifiers=Keys.Control|Keys.Alt|Keys.Shift|WindowsModifier;

        internal static string SettingsPath
        {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Trazo","settings.xml");}}

        internal static ShortcutSettings CreateDefault()
        {
            ShortcutSettings settings=new ShortcutSettings();
            settings.Shortcuts.Add(Keys.Control|Keys.Q);settings.Shortcuts.Add(Keys.PrintScreen);return settings;
        }
        internal ShortcutSettings Clone()
        {ShortcutSettings copy=new ShortcutSettings();copy.Shortcuts.AddRange(Shortcuts);return copy;}

        internal static bool IsValid(Keys shortcut)
        {
            Keys key=shortcut&Keys.KeyCode,modifiers=shortcut&Keys.Modifiers;
            if((modifiers&~SupportedModifiers)!=0)return false;
            int code=(int)key;
            if(code<8||code>254||!Enum.IsDefined(typeof(Keys),key)||key==Keys.ShiftKey||key==Keys.ControlKey||key==Keys.Menu||key==Keys.LShiftKey||key==Keys.RShiftKey||key==Keys.LControlKey||key==Keys.RControlKey||key==Keys.LMenu||key==Keys.RMenu||key==Keys.LWin||key==Keys.RWin)return false;
            return true;
        }
        internal bool Matches(int key,Keys modifiers)
        {
            Keys chord=(Keys)(key&(int)Keys.KeyCode)|(modifiers&Keys.Modifiers);
            foreach(Keys shortcut in Shortcuts)if(shortcut==chord)return true;return false;
        }
        internal static string Format(Keys shortcut)
        {
            Keys key=shortcut&Keys.KeyCode;StringBuilder text=new StringBuilder();
            if((shortcut&Keys.Control)!=0)text.Append("Ctrl + ");
            if((shortcut&Keys.Alt)!=0)text.Append("Alt + ");
            if((shortcut&Keys.Shift)!=0)text.Append("Mayús + ");
            if((shortcut&WindowsModifier)!=0)text.Append("Win + ");
            string name=key==Keys.PrintScreen?"Impr Pant":key==Keys.Space?"Espacio":key==Keys.Return?"Enter":key.ToString();
            if(key>=Keys.D0&&key<=Keys.D9)name=((int)key-(int)Keys.D0).ToString();
            text.Append(name);return text.ToString();
        }
        internal static ShortcutSettings Load(){return Load(SettingsPath);}
        internal static ShortcutSettings Load(string path)
        {
            try
            {
                FileInfo file=new FileInfo(path);if(!file.Exists)return CreateDefault();if(file.Length>32768)return CreateDefault();
                XmlReaderSettings options=new XmlReaderSettings();options.DtdProcessing=DtdProcessing.Prohibit;options.XmlResolver=null;options.MaxCharactersInDocument=32768;options.IgnoreComments=true;options.IgnoreWhitespace=true;
                ShortcutSettings result=new ShortcutSettings();HashSet<Keys> seen=new HashSet<Keys>();
                using(XmlReader reader=XmlReader.Create(path,options))
                {
                    reader.MoveToContent();if(reader.Name!="TrazoSettings"||reader.GetAttribute("version")!="1")return CreateDefault();
                    bool empty=reader.IsEmptyElement;reader.ReadStartElement("TrazoSettings");
                    if(!empty)
                    {
                        while(reader.MoveToContent()==XmlNodeType.Element)
                        {
                            if(reader.Name!="Shortcut"||result.Shortcuts.Count>=MaxShortcuts)return CreateDefault();
                            int value;if(!Int32.TryParse(reader.GetAttribute("keys"),out value))return CreateDefault();
                            Keys chord=(Keys)value;if(!IsValid(chord)||!seen.Add(chord))return CreateDefault();
                            bool shortcutEmpty=reader.IsEmptyElement;reader.ReadStartElement("Shortcut");if(!shortcutEmpty)reader.ReadEndElement();result.Shortcuts.Add(chord);
                        }
                        reader.ReadEndElement();
                    }
                    if(reader.MoveToContent()!=XmlNodeType.None)return CreateDefault();
                }
                return result;
            }
            catch(IOException){return CreateDefault();}
            catch(UnauthorizedAccessException){return CreateDefault();}
            catch(XmlException){return CreateDefault();}
            catch(ArgumentException){return CreateDefault();}
        }
        internal void Save(){Save(SettingsPath);}
        internal void Save(string path)
        {
            if(Shortcuts.Count>MaxShortcuts)throw new InvalidDataException("Puedes guardar hasta "+MaxShortcuts+" atajos.");
            HashSet<Keys> seen=new HashSet<Keys>();foreach(Keys chord in Shortcuts)if(!IsValid(chord)||!seen.Add(chord))throw new InvalidDataException("Hay atajos inválidos o repetidos.");
            string destination=Path.GetFullPath(path),directory=Path.GetDirectoryName(destination);Directory.CreateDirectory(directory);
            string temp=Path.Combine(directory,"settings."+Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                XmlWriterSettings options=new XmlWriterSettings();options.Encoding=new UTF8Encoding(false);options.Indent=true;
                using(XmlWriter writer=XmlWriter.Create(temp,options))
                {
                    writer.WriteStartDocument();writer.WriteStartElement("TrazoSettings");writer.WriteAttributeString("version","1");
                    foreach(Keys chord in Shortcuts){writer.WriteStartElement("Shortcut");writer.WriteAttributeString("keys",((int)chord).ToString(System.Globalization.CultureInfo.InvariantCulture));writer.WriteEndElement();}
                    writer.WriteEndElement();writer.WriteEndDocument();
                }
                if(File.Exists(destination))File.Replace(temp,destination,null);else File.Move(temp,destination);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
