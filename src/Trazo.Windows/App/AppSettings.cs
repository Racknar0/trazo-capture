using System;
using System.IO;
using System.Text;
using System.Xml;

namespace Trazo
{
    internal enum CloseBehavior { Ask, Tray, Exit }

    internal sealed class AppSettings
    {
        internal bool HistoryEnabled=true;
        internal int HistoryLimit=30;
        internal bool StartWithWindows=false;
        internal CloseBehavior CloseBehavior=CloseBehavior.Ask;
        internal string SaveDirectory=DefaultSaveDirectory;
        internal string ImageFormat="PNG";
        internal bool HideMainDuringCapture=true;
        internal bool StartMinimized=true;

        internal static string SettingsPath
        {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Trazo","preferences.xml");}}
        private static string DefaultSaveDirectory
        {
            get
            {
                string pictures=Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if(String.IsNullOrWhiteSpace(pictures))pictures=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Pictures");
                return Path.Combine(pictures,"Trazo");
            }
        }
        internal static bool IsValidHistoryLimit(int limit){return limit>=1&&limit<=200;}
        internal static void ValidateHistoryLimit(int limit)
        {if(!IsValidHistoryLimit(limit))throw new ArgumentOutOfRangeException("limit","El historial debe conservar entre 1 y 200 capturas.");}

        internal AppSettings Clone()
        {
            return new AppSettings{HistoryEnabled=HistoryEnabled,HistoryLimit=HistoryLimit,StartWithWindows=StartWithWindows,CloseBehavior=CloseBehavior,SaveDirectory=SaveDirectory,ImageFormat=ImageFormat,HideMainDuringCapture=HideMainDuringCapture,StartMinimized=StartMinimized};
        }
        internal static AppSettings Load(){return Load(SettingsPath);}
        internal static AppSettings Load(string path)
        {
            try
            {
                FileInfo file=new FileInfo(path);if(!file.Exists||file.Length>65536)return new AppSettings();
                XmlReaderSettings readerSettings=new XmlReaderSettings();readerSettings.DtdProcessing=DtdProcessing.Prohibit;readerSettings.XmlResolver=null;readerSettings.MaxCharactersInDocument=65536;readerSettings.IgnoreWhitespace=true;readerSettings.IgnoreComments=true;
                using(XmlReader reader=XmlReader.Create(path,readerSettings))
                {
                    reader.MoveToContent();if(reader.NodeType!=XmlNodeType.Element||reader.Name!="TrazoPreferences"||reader.GetAttribute("version")!="1")return new AppSettings();
                    AppSettings settings=new AppSettings();string value;
                    value=reader.GetAttribute("historyEnabled");if(value!=null)settings.HistoryEnabled=XmlConvert.ToBoolean(value);
                    value=reader.GetAttribute("historyLimit");if(value!=null)settings.HistoryLimit=XmlConvert.ToInt32(value);
                    value=reader.GetAttribute("startWithWindows");if(value!=null)settings.StartWithWindows=XmlConvert.ToBoolean(value);
                    value=reader.GetAttribute("closeBehavior");if(value!=null){CloseBehavior behavior;if(!Enum.TryParse<CloseBehavior>(value,false,out behavior)||!Enum.IsDefined(typeof(CloseBehavior),behavior))return new AppSettings();settings.CloseBehavior=behavior;}
                    value=reader.GetAttribute("saveDirectory");if(value!=null)settings.SaveDirectory=value;
                    value=reader.GetAttribute("imageFormat");if(value!=null)settings.ImageFormat=value;
                    value=reader.GetAttribute("hideMainDuringCapture");if(value!=null)settings.HideMainDuringCapture=XmlConvert.ToBoolean(value);
                    value=reader.GetAttribute("startMinimized");if(value!=null)settings.StartMinimized=XmlConvert.ToBoolean(value);
                    bool empty=reader.IsEmptyElement;reader.ReadStartElement("TrazoPreferences");if(!empty){reader.MoveToContent();reader.ReadEndElement();}
                    if(reader.MoveToContent()!=XmlNodeType.None)return new AppSettings();
                    settings.Validate();return settings;
                }
            }
            catch(IOException){return new AppSettings();}
            catch(UnauthorizedAccessException){return new AppSettings();}
            catch(XmlException){return new AppSettings();}
            catch(ArgumentException){return new AppSettings();}
            catch(FormatException){return new AppSettings();}
            catch(OverflowException){return new AppSettings();}
            catch(System.Security.SecurityException){return new AppSettings();}
        }
        internal void Save(){Save(SettingsPath);}
        internal void Save(string path)
        {
            Validate();
            string destination=Path.GetFullPath(path),directory=Path.GetDirectoryName(destination);
            Directory.CreateDirectory(directory);
            string temporary=Path.Combine(directory,"preferences."+Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                XmlWriterSettings writerSettings=new XmlWriterSettings();writerSettings.Encoding=new UTF8Encoding(false);writerSettings.Indent=true;
                using(XmlWriter writer=XmlWriter.Create(temporary,writerSettings))
                {
                    writer.WriteStartDocument();writer.WriteStartElement("TrazoPreferences");writer.WriteAttributeString("version","1");
                    writer.WriteAttributeString("historyEnabled",XmlConvert.ToString(HistoryEnabled));writer.WriteAttributeString("historyLimit",XmlConvert.ToString(HistoryLimit));
                    writer.WriteAttributeString("startWithWindows",XmlConvert.ToString(StartWithWindows));writer.WriteAttributeString("closeBehavior",CloseBehavior.ToString());
                    writer.WriteAttributeString("saveDirectory",SaveDirectory);writer.WriteAttributeString("imageFormat",ImageFormat);
                    writer.WriteAttributeString("hideMainDuringCapture",XmlConvert.ToString(HideMainDuringCapture));writer.WriteAttributeString("startMinimized",XmlConvert.ToString(StartMinimized));
                    writer.WriteEndElement();writer.WriteEndDocument();
                }
                if(File.Exists(destination))File.Replace(temporary,destination,null);else File.Move(temporary,destination);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        private void Validate()
        {
            ValidateHistoryLimit(HistoryLimit);
            if(!Enum.IsDefined(typeof(CloseBehavior),CloseBehavior))throw new ArgumentOutOfRangeException("CloseBehavior");
            if(String.IsNullOrWhiteSpace(SaveDirectory)||!Path.IsPathRooted(SaveDirectory))throw new ArgumentException("Elige una carpeta válida para guardar las capturas.","SaveDirectory");
            Path.GetFullPath(SaveDirectory);
            if(ImageFormat!="PNG"&&ImageFormat!="JPEG"&&ImageFormat!="BMP")throw new ArgumentException("El formato de imagen no es válido.","ImageFormat");
        }
    }
}
