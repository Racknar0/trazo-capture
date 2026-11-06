using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Trazo
{
    internal sealed class CaptureRecord
    {
        internal string Id,FullPath;
        internal DateTime CreatedUtc;
        internal int Width,Height;
    }

    internal sealed class CaptureHistoryStore
    {
        private readonly string directory;
        private readonly object sync=new object();
        private DateTime lastAddedUtc=DateTime.MinValue;
        private static readonly Regex NamePattern=new Regex(@"^TrazoCapture_(\d{8}T\d{13}Z)_([0-9a-f]{32})\.png$",RegexOptions.CultureInvariant);
        private const string DatePattern="yyyyMMdd'T'HHmmssfffffff'Z'";
        private const long MaxPixels=128L*1024*1024;
        internal static string DefaultDirectory
        {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Trazo","Captures");}}

        internal CaptureHistoryStore(string directory)
        {
            if(String.IsNullOrWhiteSpace(directory))throw new ArgumentException("La carpeta del historial no es válida.","directory");
            this.directory=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            if(this.directory.Length<3)throw new ArgumentException("El historial necesita una carpeta propia.","directory");
        }
        internal List<CaptureRecord> Load()
        {
            lock(sync){return LoadCore();}
        }
        private List<CaptureRecord> LoadCore()
        {
            List<CaptureRecord> records=new List<CaptureRecord>();
            if(!Directory.Exists(directory))return records;
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("La carpeta del historial no puede ser un enlace.");
            foreach(string path in Directory.GetFiles(directory,"TrazoCapture_*.png",SearchOption.TopDirectoryOnly))
            {
                CaptureRecord record;
                if(TryReadRecord(path,out record))records.Add(record);
            }
            records.Sort(delegate(CaptureRecord a,CaptureRecord b){int time=b.CreatedUtc.CompareTo(a.CreatedUtc);return time!=0?time:StringComparer.Ordinal.Compare(b.Id,a.Id);});
            return records;
        }
        internal CaptureRecord Add(Bitmap image,int limit)
        {
            if(image==null)throw new ArgumentNullException("image");AppSettings.ValidateHistoryLimit(limit);
            if(image.Width<1||image.Height<1||image.Width>32768||image.Height>32768||(long)image.Width*image.Height>MaxPixels)throw new ArgumentOutOfRangeException("image","La captura es demasiado grande para el historial.");
            lock(sync)
            {
                Directory.CreateDirectory(directory);
                if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("La carpeta del historial no puede ser un enlace.");
                DateTime created=DateTime.UtcNow;
                if(created<=lastAddedUtc)created=lastAddedUtc.AddTicks(1);lastAddedUtc=created;
                string id=Guid.NewGuid().ToString("N");string fileName="TrazoCapture_"+created.ToString(DatePattern,CultureInfo.InvariantCulture)+"_"+id+".png";
                string path=Path.Combine(directory,fileName),temporary=Path.Combine(directory,".capture-"+id+".tmp");
                try
                {
                    using(FileStream stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))image.Save(stream,ImageFormat.Png);
                    File.Move(temporary,path);
                }
                finally{if(File.Exists(temporary))File.Delete(temporary);}
                PruneCore(limit);
                return new CaptureRecord{Id=id,FullPath=path,CreatedUtc=created,Width=image.Width,Height=image.Height};
            }
        }
        internal void Prune(int limit)
        {
            AppSettings.ValidateHistoryLimit(limit);lock(sync){PruneCore(limit);}
        }
        private void PruneCore(int limit)
        {
            List<CaptureRecord> records=LoadCore();bool failed=false;
            for(int i=limit;i<records.Count;i++)if(!DeleteCore(records[i]))failed=true;
            if(failed)throw new IOException("No se pudieron retirar algunas capturas antiguas del historial. Cierra las imágenes que estén abiertas e inténtalo de nuevo.");
        }
        internal bool Delete(CaptureRecord record)
        {
            lock(sync){return DeleteCore(record);}
        }
        private bool DeleteCore(CaptureRecord record)
        {
            if(record==null||String.IsNullOrWhiteSpace(record.FullPath))return false;
            try
            {
                string path=Path.GetFullPath(record.FullPath);
                if(!String.Equals(Path.GetDirectoryName(path),directory,StringComparison.OrdinalIgnoreCase)||!NamePattern.IsMatch(Path.GetFileName(path)))return false;
                if(!Directory.Exists(directory)||(File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)return false;
                if(!File.Exists(path))return false;
                if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return false;
                File.Delete(path);return true;
            }
            catch(IOException){return false;}
            catch(UnauthorizedAccessException){return false;}
            catch(ArgumentException){return false;}
            catch(NotSupportedException){return false;}
            catch(System.Security.SecurityException){return false;}
        }
        internal void Clear()
        {
            lock(sync)
            {
                bool failed=false;foreach(CaptureRecord record in LoadCore())if(!DeleteCore(record))failed=true;
                if(failed)throw new IOException("No se pudieron retirar algunas capturas del historial.");
            }
        }
        private bool TryReadRecord(string path,out CaptureRecord record)
        {
            record=null;
            try
            {
                if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return false;
                Match match=NamePattern.Match(Path.GetFileName(path));if(!match.Success)return false;
                DateTime date;if(!DateTime.TryParseExact(match.Groups[1].Value,DatePattern,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out date))return false;
                int width,height;if(!TryPngSize(path,out width,out height))return false;
                record=new CaptureRecord{Id=match.Groups[2].Value,FullPath=Path.GetFullPath(path),CreatedUtc=date,Width=width,Height=height};return true;
            }
            catch(IOException){return false;}
            catch(UnauthorizedAccessException){return false;}
            catch(ArgumentException){return false;}
            catch(NotSupportedException){return false;}
        }
        private static bool TryPngSize(string path,out int width,out int height)
        {
            width=0;height=0;byte[] bytes=new byte[33];
            using(FileStream stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            {
                if(stream.Length<45)return false;
                int offset=0;while(offset<bytes.Length){int read=stream.Read(bytes,offset,bytes.Length-offset);if(read==0)return false;offset+=read;}
                stream.Seek(-12,SeekOrigin.End);byte[] terminal=new byte[12];offset=0;while(offset<terminal.Length){int read=stream.Read(terminal,offset,terminal.Length-offset);if(read==0)return false;offset+=read;}
                byte[] expected={0,0,0,0,73,69,78,68,174,66,96,130};for(int i=0;i<expected.Length;i++)if(terminal[i]!=expected[i])return false;
            }
            byte[] signature={137,80,78,71,13,10,26,10};for(int i=0;i<signature.Length;i++)if(bytes[i]!=signature[i])return false;
            if(bytes[8]!=0||bytes[9]!=0||bytes[10]!=0||bytes[11]!=13||bytes[12]!=73||bytes[13]!=72||bytes[14]!=68||bytes[15]!=82)return false;
            uint w=((uint)bytes[16]<<24)|((uint)bytes[17]<<16)|((uint)bytes[18]<<8)|bytes[19];uint h=((uint)bytes[20]<<24)|((uint)bytes[21]<<16)|((uint)bytes[22]<<8)|bytes[23];
            if(w<1||h<1||w>32768||h>32768||(long)w*h>MaxPixels)return false;
            width=(int)w;height=(int)h;return true;
        }
    }
}
