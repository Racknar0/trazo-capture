using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Trazo
{
    internal sealed class ExportOptions
    {
        internal bool Grayscale;
        internal bool Border;
        internal bool Shadow;
        internal int QuarterTurns;

        internal ExportOptions Clone()
        {
            return new ExportOptions { Grayscale=Grayscale, Border=Border, Shadow=Shadow, QuarterTurns=QuarterTurns };
        }
    }

    internal static class ImageEffects
    {
        internal static Bitmap Apply(Bitmap source, ExportOptions options)
        {
            if(source==null)throw new ArgumentNullException("source");
            if(options==null)options=new ExportOptions();
            Bitmap image=source.Clone(new Rectangle(0,0,source.Width,source.Height),PixelFormat.Format32bppArgb);
            try
            {
                int turns=((options.QuarterTurns%4)+4)%4;
                if(turns==1)image.RotateFlip(RotateFlipType.Rotate90FlipNone);
                else if(turns==2)image.RotateFlip(RotateFlipType.Rotate180FlipNone);
                else if(turns==3)image.RotateFlip(RotateFlipType.Rotate270FlipNone);
                if(options.Grayscale)
                {
                    Bitmap gray=new Bitmap(image.Width,image.Height,PixelFormat.Format32bppArgb);
                    try
                    {
                        using(Graphics g=Graphics.FromImage(gray))
                        using(ImageAttributes attributes=new ImageAttributes())
                        {
                            ColorMatrix matrix=new ColorMatrix(new float[][] {
                                new float[]{.299f,.299f,.299f,0,0},
                                new float[]{.587f,.587f,.587f,0,0},
                                new float[]{.114f,.114f,.114f,0,0},
                                new float[]{0,0,0,1,0},
                                new float[]{0,0,0,0,1}});
                            attributes.SetColorMatrix(matrix);
                            g.DrawImage(image,new Rectangle(0,0,image.Width,image.Height),0,0,image.Width,image.Height,GraphicsUnit.Pixel,attributes);
                        }
                    }
                    catch { gray.Dispose();throw; }
                    image.Dispose();image=gray;
                }
                if(options.Border)
                {
                    const int border=12;
                    Bitmap framed=new Bitmap(checked(image.Width+border*2),checked(image.Height+border*2),PixelFormat.Format32bppArgb);
                    try { using(Graphics g=Graphics.FromImage(framed)){g.Clear(Color.White);g.DrawImageUnscaled(image,border,border);} }
                    catch { framed.Dispose();throw; }
                    image.Dispose();image=framed;
                }
                if(options.Shadow)
                {
                    const int margin=16;
                    Bitmap shadow=new Bitmap(checked(image.Width+margin*2),checked(image.Height+margin*2),PixelFormat.Format32bppArgb);
                    try
                    {
                        using(Graphics g=Graphics.FromImage(shadow))
                        {
                            g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
                            // Capas de baja opacidad aproximan la caída suave de una sombra.
                            for(int spread=10;spread>=0;spread--)
                            using(Brush b=new SolidBrush(Color.FromArgb(spread==0?22:5,0,0,0)))
                                g.FillRectangle(b,margin+4-spread,margin+5-spread,image.Width+spread*2,image.Height+spread*2);
                            g.DrawImageUnscaled(image,margin,margin);
                        }
                    }
                    catch { shadow.Dispose();throw; }
                    image.Dispose();image=shadow;
                }
                Bitmap result=image;image=null;return result;
            }
            finally { if(image!=null)image.Dispose(); }
        }
    }
}
