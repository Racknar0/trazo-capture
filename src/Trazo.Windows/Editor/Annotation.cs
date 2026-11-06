using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Trazo
{
    internal enum DrawTool { Select, Rect, Ellipse, Line, Arrow, CurvedArrow, Brush, Text, Highlight, Pixelate, Blur, Image }

    internal sealed class Annotation : IDisposable
    {
        internal Guid Id = Guid.NewGuid();
        internal DrawTool Tool;
        internal Point Start, End;
        internal Color Color;
        internal float Width = 2;
        internal float FontSize = 20;
        internal string Text;
        internal readonly List<Point> Points = new List<Point>();
        internal Bitmap Patch;
        internal static Rectangle Between(Point a, Point b)
        { return Rectangle.FromLTRB(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y)); }

        internal Annotation Clone()
        {
            Annotation copy = new Annotation();
            copy.Id=Id; copy.Tool=Tool; copy.Start=Start; copy.End=End;
            copy.Color=Color; copy.Width=Width; copy.FontSize=FontSize; copy.Text=Text;
            copy.Points.AddRange(Points);
            if(Patch!=null) copy.Patch=Patch.Clone(new Rectangle(0,0,Patch.Width,Patch.Height),PixelFormat.Format32bppArgb);
            return copy;
        }

        internal Annotation Duplicate()
        {
            Annotation copy=Clone(); copy.Id=Guid.NewGuid(); copy.MoveBy(16,16); return copy;
        }

        private float StrokeWidth
        { get { return Tool==DrawTool.Highlight ? Math.Max(18,Width*5) : Math.Max(0.1f,Width); } }

        private Rectangle PatchBounds()
        {
            Rectangle bounds=Between(Start,End);
            if(bounds.Width<1 || bounds.Height<1)
                return new Rectangle(Start,Patch==null ? new Size(1,1) : Patch.Size);
            return bounds;
        }

        internal Rectangle GetBounds()
        {
            if(Patch!=null) return PatchBounds();
            if(Tool==DrawTool.Text)
            {
                using(Bitmap sample=new Bitmap(1,1))
                using(Graphics graphics=Graphics.FromImage(sample))
                using(Font font=new Font("Segoe UI",Math.Max(1,FontSize),FontStyle.Regular,GraphicsUnit.Pixel))
                {
                    SizeF measured=graphics.MeasureString(String.IsNullOrEmpty(Text)?" ":Text,font,Int32.MaxValue,StringFormat.GenericTypographic);
                    return new Rectangle(Start,new Size(Math.Max(1,(int)Math.Ceiling(measured.Width)),Math.Max(1,(int)Math.Ceiling(measured.Height))));
                }
            }
            using(GraphicsPath path=CreatePath())
            {
                if(path.PointCount==0) return new Rectangle(Start,new Size(1,1));
                RectangleF bounds=path.GetBounds();
                float pad=StrokeWidth/2f+1;
                bounds.Inflate(pad,pad);
                if(Tool==DrawTool.Arrow || Tool==DrawTool.CurvedArrow)
                {
                    PointF[] arrow=ArrowPolygon();
                    for(int i=0;i<arrow.Length;i++)
                        bounds=RectangleF.Union(bounds,new RectangleF(arrow[i].X,arrow[i].Y,0.1f,0.1f));
                }
                int left=(int)Math.Floor(bounds.Left),top=(int)Math.Floor(bounds.Top);
                return Rectangle.FromLTRB(left,top,Math.Max(left+1,(int)Math.Ceiling(bounds.Right)),Math.Max(top+1,(int)Math.Ceiling(bounds.Bottom)));
            }
        }

        internal bool HitTest(Point point,float tolerance)
        {
            if(Patch!=null || Tool==DrawTool.Text)
            {
                Rectangle bounds=GetBounds();bounds.Inflate((int)Math.Ceiling(Math.Max(0,tolerance)),(int)Math.Ceiling(Math.Max(0,tolerance)));
                return bounds.Contains(point);
            }
            using(GraphicsPath path=CreatePath())
            using(Pen pen=new Pen(System.Drawing.Color.Black,StrokeWidth+2*Math.Max(0,tolerance)))
            {
                pen.StartCap=LineCap.Round;pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
                if(path.PointCount>0 && (path.IsOutlineVisible(point,pen) || ((Tool==DrawTool.Rect || Tool==DrawTool.Ellipse) && path.IsVisible(point)))) return true;
                if(Tool==DrawTool.Arrow || Tool==DrawTool.CurvedArrow)
                {
                    using(GraphicsPath head=new GraphicsPath())
                    {head.AddPolygon(ArrowPolygon());if(head.IsVisible(point)||head.IsOutlineVisible(point,pen))return true;}
                }
                return false;
            }
        }

        private GraphicsPath CreatePath()
        {
            GraphicsPath path=new GraphicsPath();Rectangle rect=Between(Start,End);
            switch(Tool)
            {
                case DrawTool.Rect: if(rect.Width>0&&rect.Height>0)path.AddRectangle(rect);break;
                case DrawTool.Ellipse: if(rect.Width>0&&rect.Height>0)path.AddEllipse(rect);break;
                case DrawTool.CurvedArrow: path.AddBezier(Start,new Point(Start.X,End.Y),new Point((Start.X+End.X)/2,End.Y),End);break;
                case DrawTool.Brush:
                case DrawTool.Highlight:
                    if(Points.Count>1)path.AddLines(Points.ToArray());
                    else path.AddLine(Start,new Point(Start.X+1,Start.Y));
                    break;
                case DrawTool.Line:
                case DrawTool.Arrow: path.AddLine(Start,End);break;
            }
            return path;
        }

        private PointF[] ArrowPolygon()
        {
            float dx=End.X-Start.X,dy=End.Y-Start.Y;
            if(Tool==DrawTool.CurvedArrow)
            {dx=End.X-(Start.X+End.X)/2f;dy=0; if(Math.Abs(dx)<0.01f){dx=End.X-Start.X;dy=End.Y-Start.Y;}}
            double length=Math.Sqrt(dx*dx+dy*dy);
            if(length<0.01){dx=1;dy=0;length=1;}
            float ux=(float)(dx/length),uy=(float)(dy/length),size=Math.Max(0.1f,Width);
            return new PointF[]{new PointF(End.X,End.Y),new PointF(End.X-ux*size*5-uy*size*2,End.Y-uy*size*5+ux*size*2),new PointF(End.X-ux*size*5+uy*size*2,End.Y-uy*size*5-ux*size*2)};
        }

        internal void MoveBy(int dx,int dy)
        {
            Start.Offset(dx,dy);End.Offset(dx,dy);
            for(int i=0;i<Points.Count;i++)Points[i]=new Point(Points[i].X+dx,Points[i].Y+dy);
        }

        internal void ResizeTo(Rectangle bounds)
        {
            if(bounds.Width<1 || bounds.Height<1)throw new ArgumentOutOfRangeException("bounds");
            Rectangle old=GetBounds();
            float sx=bounds.Width/(float)Math.Max(1,old.Width),sy=bounds.Height/(float)Math.Max(1,old.Height);
            if(Patch!=null)
            {Start=bounds.Location;End=new Point(bounds.Right,bounds.Bottom);return;}
            if(Tool==DrawTool.Text)
            {
                Start=bounds.Location;End=Start;
                FontSize=Math.Max(6,Math.Min(512,FontSize*Math.Min(sx,sy)));return;
            }
            Start=MapPoint(Start,old,bounds,sx,sy);End=MapPoint(End,old,bounds,sx,sy);
            for(int i=0;i<Points.Count;i++)Points[i]=MapPoint(Points[i],old,bounds,sx,sy);
            Width=Math.Max(0.5f,Math.Min(128,Width*Math.Min(sx,sy)));
        }

        private static Point MapPoint(Point point,Rectangle old,Rectangle bounds,float sx,float sy)
        {return new Point(bounds.Left+(int)Math.Round((point.X-old.Left)*sx),bounds.Top+(int)Math.Round((point.Y-old.Top)*sy));}

        internal void Draw(Graphics g)
        {
            Rectangle r = Between(Start, End);
            if (Patch != null) { g.DrawImage(Patch,PatchBounds(),0,0,Patch.Width,Patch.Height,GraphicsUnit.Pixel); return; }
            using (Pen pen = new Pen(Tool == DrawTool.Highlight ? Color.FromArgb(85, Color) : Color, Tool == DrawTool.Highlight ? Math.Max(18, Width * 5) : Width))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                switch (Tool)
                {
                    case DrawTool.Rect: if (r.Width>0 && r.Height>0) g.DrawRectangle(pen, r); break;
                    case DrawTool.Ellipse: if (r.Width>0 && r.Height>0) g.DrawEllipse(pen, r); break;
                    case DrawTool.Line: g.DrawLine(pen, Start, End); break;
                    case DrawTool.Arrow:
                    case DrawTool.CurvedArrow:
                        using (AdjustableArrowCap cap = new AdjustableArrowCap(4,5,true))
                        {
                            pen.CustomEndCap = cap;
                            if (Tool == DrawTool.Arrow) g.DrawLine(pen,Start,End);
                            else g.DrawBezier(pen, Start, new Point(Start.X,End.Y),new Point((Start.X+End.X)/2,End.Y),End);
                        }
                        break;
                    case DrawTool.Brush:
                    case DrawTool.Highlight:
                        if (Points.Count > 1) g.DrawLines(pen,Points.ToArray());
                        else g.DrawLine(pen,Start,new Point(Start.X+1,Start.Y));
                        break;
                    case DrawTool.Text:
                        using (Font font = new Font("Segoe UI",FontSize,FontStyle.Regular,GraphicsUnit.Pixel))
                        using (Brush brush = new SolidBrush(Color))
                            g.DrawString(Text ?? "",font,brush,Start,StringFormat.GenericTypographic);
                        break;
                }
            }
        }

        internal static Bitmap Pixelate(Bitmap source, int block)
        {
            Bitmap result = new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                for (int y=0;y<source.Height;y+=block)
                for (int x=0;x<source.Width;x+=block)
                {
                    int w=Math.Min(block,source.Width-x),h=Math.Min(block,source.Height-y);
                    long red=0,green=0,blue=0; int count=0;
                    for(int yy=y;yy<y+h;yy+=2)
                    for(int xx=x;xx<x+w;xx+=2) { Color c=source.GetPixel(xx,yy);red+=c.R;green+=c.G;blue+=c.B;count++; }
                    using(Brush b=new SolidBrush(Color.FromArgb((int)(red/count),(int)(green/count),(int)(blue/count))))
                        g.FillRectangle(b,x,y,w,h);
                }
            }
            return result;
        }

        internal static Bitmap Blur(Bitmap source, int radius)
        {
            Bitmap result = source.Clone(new Rectangle(0,0,source.Width,source.Height),PixelFormat.Format32bppArgb);
            Rectangle rect=new Rectangle(0,0,result.Width,result.Height);
            BitmapData bits=result.LockBits(rect,ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
            try
            {
                int stride=bits.Stride,w=result.Width,h=result.Height;
                byte[] a=new byte[stride*h],b=new byte[stride*h];Marshal.Copy(bits.Scan0,a,0,a.Length);
                for(int pass=0;pass<3;pass++)
                {
                    for(int y=0;y<h;y++) for(int c=0;c<3;c++)
                    {
                        int sum=0;for(int k=-radius;k<=radius;k++)sum+=a[y*stride+Math.Max(0,Math.Min(w-1,k))*4+c];
                        for(int x=0;x<w;x++) { b[y*stride+x*4+c]=(byte)(sum/(2*radius+1));sum-=a[y*stride+Math.Max(0,x-radius)*4+c];sum+=a[y*stride+Math.Min(w-1,x+radius+1)*4+c]; }
                    }
                    for(int x=0;x<w;x++) for(int c=0;c<3;c++)
                    {
                        int sum=0;for(int k=-radius;k<=radius;k++)sum+=b[Math.Max(0,Math.Min(h-1,k))*stride+x*4+c];
                        for(int y=0;y<h;y++) { a[y*stride+x*4+c]=(byte)(sum/(2*radius+1));sum-=b[Math.Max(0,y-radius)*stride+x*4+c];sum+=b[Math.Min(h-1,y+radius+1)*stride+x*4+c]; }
                    }
                }
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)a[y*stride+x*4+3]=255;
                Marshal.Copy(a,0,bits.Scan0,a.Length);
            }
            finally { result.UnlockBits(bits); }
            return result;
        }
        public void Dispose() { if(Patch!=null) { Patch.Dispose();Patch=null; } }
    }
}
