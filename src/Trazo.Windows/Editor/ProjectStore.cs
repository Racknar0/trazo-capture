using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace Trazo
{
    internal sealed class ProjectData : IDisposable
    {
        internal Bitmap Screenshot;
        internal Rectangle VirtualBounds;
        internal Rectangle Selection;
        internal Annotation[] Annotations;
        internal ExportOptions Effects;

        public void Dispose()
        {
            if(Screenshot!=null){Screenshot.Dispose();Screenshot=null;}
            if(Annotations!=null){foreach(Annotation annotation in Annotations)if(annotation!=null)annotation.Dispose();Annotations=null;}
        }
    }

    internal static class ProjectStore
    {
        private const long MaxFileBytes=192L*1024*1024;
        private const long MaxImageBytes=96L*1024*1024;
        private const long MaxImagePixels=64L*1024*1024;
        private const long MaxTotalPixels=128L*1024*1024;
        private const int MaxDimension=32768;
        private const int MaxAnnotations=10000;
        private const int MaxPoints=1000000;
        private const int MaxText=65536;
        private static readonly CultureInfo Invariant=CultureInfo.InvariantCulture;

        internal static void Save(string path,Bitmap screenshot,Rectangle virtualBounds,Rectangle selection,Annotation[] annotations,ExportOptions effects)
        {
            if(String.IsNullOrWhiteSpace(path))throw new ArgumentException("Indica dónde guardar el proyecto.","path");
            if(screenshot==null)throw new ArgumentNullException("screenshot");
            if(annotations==null)annotations=new Annotation[0];
            if(effects==null)effects=new ExportOptions();
            ValidateCanvas(screenshot.Width,screenshot.Height,virtualBounds,selection);
            if(annotations.Length>MaxAnnotations)throw Invalid("El proyecto contiene demasiadas anotaciones.");
            long totalPixels=(long)screenshot.Width*screenshot.Height;int pointCount=0;
            HashSet<Guid> identities=new HashSet<Guid>();
            foreach(Annotation annotation in annotations)
            {
                ValidateAnnotation(annotation,identities,ref pointCount);
                if(annotation.Patch!=null){ValidateImageSize(annotation.Patch.Width,annotation.Patch.Height);totalPixels+=(long)annotation.Patch.Width*annotation.Patch.Height;}
                if(totalPixels>MaxTotalPixels)throw Invalid("Las imágenes del proyecto son demasiado grandes.");
            }
            string destination=Path.GetFullPath(path);
            string directory=Path.GetDirectoryName(destination);
            string temp=Path.Combine(directory,"."+Path.GetFileName(destination)+"."+Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                XmlWriterSettings settings=new XmlWriterSettings();settings.Encoding=new UTF8Encoding(false);settings.Indent=true;settings.CloseOutput=true;
                using(FileStream file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    using(XmlWriter writer=XmlWriter.Create(file,settings))
                    {
                        writer.WriteStartDocument();writer.WriteStartElement("TrazoProject");writer.WriteAttributeString("version","1");
                        WriteRectangle(writer,"Canvas",virtualBounds);WriteRectangle(writer,"Selection",selection);
                        writer.WriteStartElement("Effects");writer.WriteAttributeString("grayscale",XmlConvert.ToString(effects.Grayscale));writer.WriteAttributeString("border",XmlConvert.ToString(effects.Border));writer.WriteAttributeString("shadow",XmlConvert.ToString(effects.Shadow));writer.WriteAttributeString("quarterTurns",Int(((effects.QuarterTurns%4)+4)%4));writer.WriteEndElement();
                        WriteImage(writer,"Screenshot",screenshot);
                        writer.WriteStartElement("Annotations");writer.WriteAttributeString("count",Int(annotations.Length));
                        foreach(Annotation annotation in annotations)WriteAnnotation(writer,annotation);
                        writer.WriteEndElement();writer.WriteEndElement();writer.WriteEndDocument();
                    }
                }
                if(new FileInfo(temp).Length>MaxFileBytes)throw Invalid("El archivo de proyecto excede el tamaño permitido.");
                if(File.Exists(destination))File.Replace(temp,destination,null);
                else File.Move(temp,destination);
            }
            finally { if(File.Exists(temp))File.Delete(temp); }
        }

        internal static ProjectData Load(string path)
        {
            if(String.IsNullOrWhiteSpace(path))throw new ArgumentException("Elige un proyecto de Trazo.","path");
            FileInfo fileInfo=new FileInfo(path);
            if(!fileInfo.Exists)throw new FileNotFoundException("No se encontró el proyecto de Trazo.",path);
            if(fileInfo.Length>MaxFileBytes)throw Invalid("El archivo de proyecto es demasiado grande.");
            ProjectData data=new ProjectData();List<Annotation> annotations=new List<Annotation>();
            try
            {
                XmlReaderSettings settings=new XmlReaderSettings();settings.DtdProcessing=DtdProcessing.Prohibit;settings.XmlResolver=null;settings.IgnoreComments=true;settings.IgnoreWhitespace=true;settings.CloseInput=true;settings.MaxCharactersInDocument=MaxFileBytes;settings.MaxCharactersFromEntities=1024;
                using(FileStream file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
                using(XmlReader reader=XmlReader.Create(file,settings))
                {
                    reader.MoveToContent();RequireElement(reader,"TrazoProject");
                    if(Required(reader,"version")!="1")throw Invalid("Esta versión del proyecto no es compatible con Trazo.");
                    reader.ReadStartElement("TrazoProject");
                    data.VirtualBounds=ReadRectangle(reader,"Canvas");data.Selection=ReadRectangle(reader,"Selection");
                    RequireElement(reader,"Effects");
                    data.Effects=new ExportOptions { Grayscale=ReadBool(reader,"grayscale"),Border=ReadBool(reader,"border"),Shadow=ReadBool(reader,"shadow"),QuarterTurns=ReadInt(reader,"quarterTurns",0,3) };
                    ReadEmptyElement(reader,"Effects");
                    long pixels=0;data.Screenshot=ReadImage(reader,"Screenshot",ref pixels);
                    ValidateCanvas(data.Screenshot.Width,data.Screenshot.Height,data.VirtualBounds,data.Selection);
                    RequireElement(reader,"Annotations");int expected=ReadInt(reader,"count",0,MaxAnnotations);
                    bool empty=reader.IsEmptyElement;reader.ReadStartElement("Annotations");int points=0;HashSet<Guid> identities=new HashSet<Guid>();
                    if(!empty)
                    {
                        while(reader.MoveToContent()==XmlNodeType.Element)
                        {
                            if(annotations.Count>=MaxAnnotations)throw Invalid("El proyecto contiene demasiadas anotaciones.");
                            Annotation annotation=ReadAnnotation(reader,ref pixels,ref points,identities);annotations.Add(annotation);
                        }
                        reader.ReadEndElement();
                    }
                    if(expected!=annotations.Count)throw Invalid("La lista de anotaciones del proyecto está incompleta.");
                    reader.MoveToContent();reader.ReadEndElement();
                    if(reader.MoveToContent()!=XmlNodeType.None)throw Invalid("El proyecto contiene datos inesperados.");
                }
                data.Annotations=annotations.ToArray();return data;
            }
            catch
            {
                foreach(Annotation annotation in annotations)annotation.Dispose();data.Dispose();throw;
            }
        }

        private static void WriteAnnotation(XmlWriter writer,Annotation annotation)
        {
            writer.WriteStartElement("Annotation");writer.WriteAttributeString("id",annotation.Id.ToString("D"));writer.WriteAttributeString("tool",annotation.Tool.ToString());
            writer.WriteAttributeString("sx",Int(annotation.Start.X));writer.WriteAttributeString("sy",Int(annotation.Start.Y));writer.WriteAttributeString("ex",Int(annotation.End.X));writer.WriteAttributeString("ey",Int(annotation.End.Y));
            writer.WriteAttributeString("color",Int(annotation.Color.ToArgb()));writer.WriteAttributeString("width",annotation.Width.ToString("R",Invariant));writer.WriteAttributeString("fontSize",annotation.FontSize.ToString("R",Invariant));
            writer.WriteElementString("Text",annotation.Text??String.Empty);
            writer.WriteStartElement("Points");writer.WriteAttributeString("count",Int(annotation.Points.Count));
            foreach(Point point in annotation.Points){writer.WriteStartElement("P");writer.WriteAttributeString("x",Int(point.X));writer.WriteAttributeString("y",Int(point.Y));writer.WriteEndElement();}
            writer.WriteEndElement();if(annotation.Patch!=null)WriteImage(writer,"Patch",annotation.Patch);writer.WriteEndElement();
        }

        private static Annotation ReadAnnotation(XmlReader reader,ref long pixels,ref int pointCount,HashSet<Guid> identities)
        {
            RequireElement(reader,"Annotation");Annotation annotation=new Annotation();
            try
            {
                Guid id;if(!Guid.TryParse(Required(reader,"id"),out id)||id==Guid.Empty)throw Invalid("Una anotación tiene un identificador inválido.");annotation.Id=id;
                DrawTool tool;if(!Enum.TryParse<DrawTool>(Required(reader,"tool"),false,out tool)||!Enum.IsDefined(typeof(DrawTool),tool)||tool==DrawTool.Select)throw Invalid("Una herramienta del proyecto no es válida.");annotation.Tool=tool;
                annotation.Start=new Point(ReadCoordinate(reader,"sx"),ReadCoordinate(reader,"sy"));annotation.End=new Point(ReadCoordinate(reader,"ex"),ReadCoordinate(reader,"ey"));
                annotation.Color=Color.FromArgb(ReadInt(reader,"color",Int32.MinValue,Int32.MaxValue));annotation.Width=ReadFloat(reader,"width",.5f,256f);annotation.FontSize=ReadFloat(reader,"fontSize",4f,1024f);
                reader.ReadStartElement("Annotation");RequireElement(reader,"Text");annotation.Text=reader.ReadElementContentAsString();if(annotation.Text.Length>MaxText)throw Invalid("Un texto del proyecto es demasiado largo.");
                RequireElement(reader,"Points");int expected=ReadInt(reader,"count",0,MaxPoints);bool empty=reader.IsEmptyElement;reader.ReadStartElement("Points");
                if(!empty)
                {
                    while(reader.MoveToContent()==XmlNodeType.Element)
                    {
                        RequireElement(reader,"P");if(++pointCount>MaxPoints)throw Invalid("El proyecto contiene demasiados puntos de dibujo.");
                        annotation.Points.Add(new Point(ReadCoordinate(reader,"x"),ReadCoordinate(reader,"y")));ReadEmptyElement(reader,"P");
                    }
                    reader.ReadEndElement();
                }
                if(annotation.Points.Count!=expected)throw Invalid("Los puntos de una anotación están incompletos.");
                if(reader.MoveToContent()==XmlNodeType.Element&&reader.Name=="Patch")annotation.Patch=ReadImage(reader,"Patch",ref pixels);
                reader.MoveToContent();reader.ReadEndElement();
                int ignoredPoints=0;ValidateAnnotation(annotation,identities,ref ignoredPoints);return annotation;
            }
            catch{annotation.Dispose();throw;}
        }

        private static void ValidateAnnotation(Annotation annotation,HashSet<Guid> identities,ref int points)
        {
            if(annotation==null||annotation.Id==Guid.Empty||!identities.Add(annotation.Id))throw Invalid("La lista de anotaciones contiene identificadores repetidos o inválidos.");
            if(!Enum.IsDefined(typeof(DrawTool),annotation.Tool)||annotation.Tool==DrawTool.Select)throw Invalid("Una herramienta del proyecto no es válida.");
            ValidateCoordinate(annotation.Start.X);ValidateCoordinate(annotation.Start.Y);ValidateCoordinate(annotation.End.X);ValidateCoordinate(annotation.End.Y);
            if(Single.IsNaN(annotation.Width)||Single.IsInfinity(annotation.Width)||annotation.Width<.5f||annotation.Width>256f||Single.IsNaN(annotation.FontSize)||Single.IsInfinity(annotation.FontSize)||annotation.FontSize<4f||annotation.FontSize>1024f)throw Invalid("El tamaño de una anotación no es válido.");
            if(annotation.Text!=null&&annotation.Text.Length>MaxText)throw Invalid("Un texto del proyecto es demasiado largo.");
            points=checked(points+annotation.Points.Count);if(points>MaxPoints)throw Invalid("El proyecto contiene demasiados puntos de dibujo.");
            foreach(Point point in annotation.Points){ValidateCoordinate(point.X);ValidateCoordinate(point.Y);}
            if((annotation.Tool==DrawTool.Image||annotation.Tool==DrawTool.Pixelate||annotation.Tool==DrawTool.Blur)&&annotation.Patch==null)throw Invalid("Faltan los píxeles de una imagen o efecto.");
        }

        private static void WriteImage(XmlWriter writer,string name,Bitmap bitmap)
        {
            using(MemoryStream stream=new MemoryStream())
            {
                bitmap.Save(stream,ImageFormat.Png);if(stream.Length>MaxImageBytes)throw Invalid("Una imagen del proyecto es demasiado grande.");
                writer.WriteStartElement(name);writer.WriteAttributeString("encoding","png");writer.WriteAttributeString("width",Int(bitmap.Width));writer.WriteAttributeString("height",Int(bitmap.Height));
                byte[] buffer=stream.GetBuffer();writer.WriteBase64(buffer,0,checked((int)stream.Length));writer.WriteEndElement();
            }
        }

        private static Bitmap ReadImage(XmlReader reader,string name,ref long totalPixels)
        {
            RequireElement(reader,name);if(Required(reader,"encoding")!="png")throw Invalid("El formato de una imagen no es compatible.");
            int width=ReadInt(reader,"width",1,MaxDimension),height=ReadInt(reader,"height",1,MaxDimension);ValidateImageSize(width,height);
            totalPixels+=(long)width*height;if(totalPixels>MaxTotalPixels)throw Invalid("Las imágenes del proyecto son demasiado grandes.");
            using(MemoryStream bytes=new MemoryStream())
            {
                byte[] buffer=new byte[8192];int read;
                while((read=reader.ReadElementContentAsBase64(buffer,0,buffer.Length))>0){if(bytes.Length+read>MaxImageBytes)throw Invalid("Una imagen del proyecto es demasiado grande.");bytes.Write(buffer,0,read);}
                if(bytes.Length<33)throw Invalid("Una imagen del proyecto está incompleta.");
                byte[] content=bytes.GetBuffer();
                if(content[0]!=137||content[1]!=80||content[2]!=78||content[3]!=71||content[4]!=13||content[5]!=10||content[6]!=26||content[7]!=10||content[8]!=0||content[9]!=0||content[10]!=0||content[11]!=13||content[12]!=73||content[13]!=72||content[14]!=68||content[15]!=82)throw Invalid("Una imagen del proyecto no es un PNG válido.");
                long pngWidth=ReadBigEndian(content,16),pngHeight=ReadBigEndian(content,20);
                if(pngWidth!=width||pngHeight!=height)throw Invalid("Las dimensiones de una imagen no coinciden con el proyecto.");
                bytes.Position=0;
                using(Image decoded=Image.FromStream(bytes,true,true))
                {
                    if(decoded.Width!=width||decoded.Height!=height)throw Invalid("Las dimensiones de una imagen no coinciden con el proyecto.");
                    Bitmap bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb);
                    try{using(Graphics g=Graphics.FromImage(bitmap)){g.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;g.DrawImageUnscaled(decoded,0,0);}return bitmap;}
                    catch{bitmap.Dispose();throw;}
                }
            }
        }

        private static long ReadBigEndian(byte[] bytes,int offset){return ((long)bytes[offset]<<24)|((long)bytes[offset+1]<<16)|((long)bytes[offset+2]<<8)|bytes[offset+3];}
        private static void ValidateImageSize(int width,int height){if(width<1||height<1||width>MaxDimension||height>MaxDimension||(long)width*height>MaxImagePixels)throw Invalid("Las dimensiones de una imagen del proyecto son demasiado grandes.");}
        private static void ValidateCanvas(int width,int height,Rectangle bounds,Rectangle selection)
        {
            ValidateImageSize(width,height);ValidateCoordinate(bounds.X);ValidateCoordinate(bounds.Y);
            if(bounds.Width!=width||bounds.Height!=height||selection.X<0||selection.Y<0||selection.Width<1||selection.Height<1||(long)selection.X+selection.Width>width||(long)selection.Y+selection.Height>height)throw Invalid("El área seleccionada no coincide con la captura del proyecto.");
        }
        private static void ValidateCoordinate(int value){if(value < -1000000||value>1000000)throw Invalid("Una coordenada del proyecto no es válida.");}
        private static int ReadCoordinate(XmlReader reader,string name){return ReadInt(reader,name,-1000000,1000000);}
        private static void WriteRectangle(XmlWriter writer,string name,Rectangle rect){writer.WriteStartElement(name);writer.WriteAttributeString("x",Int(rect.X));writer.WriteAttributeString("y",Int(rect.Y));writer.WriteAttributeString("width",Int(rect.Width));writer.WriteAttributeString("height",Int(rect.Height));writer.WriteEndElement();}
        private static Rectangle ReadRectangle(XmlReader reader,string name){RequireElement(reader,name);Rectangle result=new Rectangle(ReadCoordinate(reader,"x"),ReadCoordinate(reader,"y"),ReadInt(reader,"width",1,MaxDimension),ReadInt(reader,"height",1,MaxDimension));ReadEmptyElement(reader,name);return result;}
        private static void ReadEmptyElement(XmlReader reader,string name){bool empty=reader.IsEmptyElement;reader.ReadStartElement(name);if(!empty){reader.MoveToContent();reader.ReadEndElement();}}
        private static void RequireElement(XmlReader reader,string name){if(reader.MoveToContent()!=XmlNodeType.Element||reader.Name!=name||reader.NamespaceURI.Length!=0)throw Invalid("El proyecto contiene una sección inesperada: "+name+".");}
        private static string Required(XmlReader reader,string name){string value=reader.GetAttribute(name);if(value==null)throw Invalid("Falta un dato del proyecto: "+name+".");return value;}
        private static int ReadInt(XmlReader reader,string name,int minimum,int maximum){int value;if(!Int32.TryParse(Required(reader,name),NumberStyles.Integer,Invariant,out value)||value<minimum||value>maximum)throw Invalid("Un número del proyecto no es válido: "+name+".");return value;}
        private static float ReadFloat(XmlReader reader,string name,float minimum,float maximum){float value;if(!Single.TryParse(Required(reader,name),NumberStyles.Float,Invariant,out value)||Single.IsNaN(value)||Single.IsInfinity(value)||value<minimum||value>maximum)throw Invalid("Un tamaño del proyecto no es válido: "+name+".");return value;}
        private static bool ReadBool(XmlReader reader,string name){string value=Required(reader,name);if(value=="true")return true;if(value=="false")return false;throw Invalid("Una opción del proyecto no es válida: "+name+".");}
        private static string Int(int value){return value.ToString(Invariant);}
        private static InvalidDataException Invalid(string message){return new InvalidDataException(message);}
    }
}
