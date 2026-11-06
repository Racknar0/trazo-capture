using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Trazo
{
    internal sealed partial class OverlayForm : Form
    {
        private readonly Bitmap screenshot;
        private Rectangle selection;
        private bool ready;
        private Point anchor, pointer;
        private Rectangle original;
        private int dragMode, resizeHandle=-1;
        private DrawTool tool=DrawTool.Select;
        private Color ink=Color.FromArgb(255,55,55);
        private float lineWidth=2, textSize=22;
        private Annotation pending;
        private readonly List<Annotation> all=new List<Annotation>();
        private readonly List<Annotation[]> history=new List<Annotation[]>();
        private int historyIndex;
        private readonly Panel drawbar, operbar, config;
        private readonly ToolTip tips=new ToolTip();
        private readonly List<GlyphButton> tools=new List<GlyphButton>();
        private TextBox input;
        private bool exporting;
        internal event Action<Bitmap> ImageExported;
        internal string DefaultSaveDirectory;
        internal string DefaultImageFormat="PNG";
        internal static readonly Color Blue=Color.FromArgb(42,163,221);

        internal OverlayForm(Bitmap image, Rectangle virtualBounds) : this(image,virtualBounds,new CaptureWindow[0]) { }
        internal OverlayForm(Bitmap image, Rectangle virtualBounds, CaptureWindow[] windows)
        {
            screenshot=image;
            captureWindows=windows ?? new CaptureWindow[0];
            AutoScaleMode=AutoScaleMode.None; FormBorderStyle=FormBorderStyle.None;
            StartPosition=FormStartPosition.Manual; Bounds=virtualBounds; TopMost=true;
            ShowInTaskbar=false; DoubleBuffered=true; KeyPreview=true;
            Text="Trazo · Captura de pantalla"; Cursor=Cursors.Cross;
            pointer=new Point(Math.Max(0,Math.Min(image.Width-1,System.Windows.Forms.Cursor.Position.X-virtualBounds.Left)),Math.Max(0,Math.Min(image.Height-1,System.Windows.Forms.Cursor.Position.Y-virtualBounds.Top)));
            BackColor=Color.Black; Font=new Font("Segoe UI",10);
            history.Add(new Annotation[0]);
            drawbar=MakePanel(); operbar=MakePanel(); config=MakePanel();
            AddTool("select","Seleccionar y editar marcas (V). Doble clic para editar texto.",DrawTool.Select);
            AddTool("rect","Rectángulo (R)",DrawTool.Rect);
            AddTool("ellipse","Elipse (E)",DrawTool.Ellipse);
            AddTool("line","Línea (L)",DrawTool.Line);
            AddTool("arrow","Flecha (A)",DrawTool.Arrow);
            AddTool("curve","Flecha curva",DrawTool.CurvedArrow);
            AddTool("brush","Pincel (P)",DrawTool.Brush);
            AddTool("text","Texto (T)",DrawTool.Text);
            AddTool("highlight","Resaltador (H)",DrawTool.Highlight);
            AddTool("pixel","Pixelar / difuminar (M). Clic derecho para cambiar.",DrawTool.Pixelate);
            tools[tools.Count-1].MouseUp+=delegate(object sender,MouseEventArgs e) {
                if(e.Button!=MouseButtons.Right)return;
                ContextMenuStrip menu=new ContextMenuStrip();
                menu.Items.Add("Pixelar",null,delegate { SetTool(DrawTool.Pixelate); });
                menu.Items.Add("Difuminar",null,delegate { SetTool(DrawTool.Blur); });
                menu.Closed+=delegate { menu.Dispose(); };menu.Show((Control)sender,new Point(0,32));
            };
            AddSeparator(drawbar,false);
            AddButton(drawbar,"undo","Deshacer (Ctrl+Z)",Undo,false);
            AddButton(drawbar,"erase","Limpiar anotaciones",ClearAnnotations,false);
            AddSeparator(drawbar,false);
            AddButton(drawbar,"save","Guardar (Ctrl+S)",SaveImage,false);
            AddButton(operbar,"cancel","Cancelar (Esc)",delegate{Close();},true);
            AddButton(operbar,"capture","Volver a seleccionar",ResetSelection,true);
            AddButton(operbar,"fullscreen","Seleccionar toda la pantalla (Ctrl+A)",FullScreen,true);
            AddSeparator(operbar,true);
            AddButton(operbar,"copy","Copiar y terminar (Ctrl+C / Enter)",CopyImage,true);
            AddButton(operbar,"more","Más: imágenes, capas, proyectos y efectos",ShowEditorMenu,true);
            BuildConfig();
            Shown+=delegate{ UpdateHoverRegion();PlaceBars();Activate(); Focus(); };
        }

        private Panel MakePanel()
        {
            Panel p=new Panel(); p.Size=Size.Empty; p.BackColor=Color.White; p.Visible=false; p.TabStop=false;
            p.Paint+=delegate(object sender,PaintEventArgs e){using(Pen pen=new Pen(Color.FromArgb(210,218,224)))e.Graphics.DrawRectangle(pen,0,0,p.Width-1,p.Height-1);};
            Controls.Add(p);return p;
        }
        private void AddTool(string glyph,string label,DrawTool value)
        {
            GlyphButton button=AddButton(drawbar,glyph,label,delegate{SetTool(value);},false);
            button.Tag=value;button.Selected=value==tool; tools.Add(button);
        }
        private GlyphButton AddButton(Panel panel,string glyph,string label,Action action,bool vertical)
        {
            GlyphButton b=new GlyphButton(glyph);b.AccessibleName=label;
            if(vertical){b.Location=new Point(2,panel.Height<5?2:panel.Height-2);panel.Size=new Size(36,b.Bottom+2);}
            else { b.Location=new Point(panel.Width<5?2:panel.Width-2,2);panel.Size=new Size(b.Right+2,36); }
            b.Click+=delegate{CommitText();action();};tips.SetToolTip(b,label);panel.Controls.Add(b);return b;
        }
        private void AddSeparator(Panel p,bool vertical)
        {
            Panel sep=new Panel();sep.BackColor=Color.FromArgb(224,230,235);
            if(vertical){sep.Bounds=new Rectangle(7,p.Height,22,1);p.Height+=5;}
            else {sep.Bounds=new Rectangle(p.Width+2,8,1,20);p.Width+=8;}
            p.Controls.Add(sep);
        }
        private void BuildConfig()
        {
            config.Size=new Size(458,38);
            Color[] colors={Color.FromArgb(255,55,55),Color.FromArgb(255,152,0),Color.FromArgb(255,220,0),Color.FromArgb(44,196,92),Color.FromArgb(0,165,255),Color.FromArgb(130,85,225),Color.White,Color.Black};
            for(int i=0;i<colors.Length;i++)
            {
                Color c=colors[i];Panel swatch=new Panel();swatch.BackColor=c;swatch.Bounds=new Rectangle(10+i*22,10,16,16);swatch.Cursor=Cursors.Hand;
                swatch.Paint+=delegate(object sender,PaintEventArgs e){using(Pen p=new Pen(c.ToArgb()==ink.ToArgb()?Blue:Color.FromArgb(180,190,200),c.ToArgb()==ink.ToArgb()?2:1))e.Graphics.DrawRectangle(p,0,0,15,15);};
                swatch.Click+=delegate {CommitText();ink=c;ApplySelectedStyle(true,false,false);config.Invalidate(true);Focus();};tips.SetToolTip(swatch,"Color");config.Controls.Add(swatch);
            }
            GlyphButton custom=new GlyphButton("palette");custom.Bounds=new Rectangle(188,3,30,30);custom.Click+=delegate{CommitText();using(ColorDialog d=new ColorDialog()){d.Color=ink;d.FullOpen=true;if(d.ShowDialog(this)==DialogResult.OK){ink=d.Color;ApplySelectedStyle(true,false,false);}}config.Invalidate(true);};tips.SetToolTip(custom,"Más colores");config.Controls.Add(custom);
            for(int i=0;i<3;i++)
            {
                float width=new float[]{2,4,7}[i];GlyphButton b=new GlyphButton("dot"+i);b.Bounds=new Rectangle(224+i*28,3,28,30);
                b.Click+=delegate{CommitText();lineWidth=width;ApplySelectedStyle(false,true,false);foreach(Control c in config.Controls)if(c is GlyphButton && ((GlyphButton)c).Glyph.StartsWith("dot")){((GlyphButton)c).Selected=(c==b);c.Invalidate();}Focus();};
                b.Selected=i==0;tips.SetToolTip(b,"Grosor "+width+" px");config.Controls.Add(b);
            }
            Label a=new Label();a.Text="A";a.ForeColor=Blue;a.Bounds=new Rectangle(319,8,20,23);config.Controls.Add(a);
            ComboBox size=new ComboBox();size.DropDownStyle=ComboBoxStyle.DropDownList;size.Bounds=new Rectangle(342,7,62,26);size.Font=new Font("Segoe UI",9);
            foreach(int n in new int[]{12,16,18,22,28,36,48,64})size.Items.Add(n);size.SelectedItem=22;
            size.SelectedIndexChanged+=delegate{if(syncingConfig)return;CommitText();textSize=Convert.ToSingle(size.SelectedItem);ApplySelectedStyle(false,false,true);Focus();};tips.SetToolTip(size,"Tamaño de texto (px)");config.Controls.Add(size);
            GlyphButton select=new GlyphButton("select");select.Bounds=new Rectangle(414,3,30,30);select.Click+=delegate{SetTool(DrawTool.Select);};tips.SetToolTip(select,"Mover o ajustar selección (V)");config.Controls.Add(select);
        }
        private void SetTool(DrawTool value)
        {
            CommitText();tool=tool==value?DrawTool.Select:value;
            if(tool!=DrawTool.Select)selectedId=null;
            foreach(GlyphButton b in tools){DrawTool v=(DrawTool)b.Tag;b.Selected=(v==tool || (v==DrawTool.Pixelate&&tool==DrawTool.Blur));b.Invalidate();}
            config.Visible=ready&&(tool!=DrawTool.Select||SelectedAnnotation!=null);PlaceBars();Focus();Invalidate();
        }
        private void PlaceBars()
        {
            if(!ready){drawbar.Hide();operbar.Hide();config.Hide();return;}
            Rectangle viewport=Screen.FromPoint(new Point(Left+selection.Right-1,Top+selection.Bottom-1)).Bounds;
            viewport.Offset(-Left,-Top);viewport.Intersect(ClientRectangle);
            if(viewport.Width<1)viewport=ClientRectangle;
            int barsWidth=Math.Max(drawbar.Width,config.Visible?config.Width:0);
            int x=Math.Max(viewport.Left+3,Math.Min(selection.Right-drawbar.Width,viewport.Right-barsWidth-3));
            int y=selection.Bottom+8;
            int needed=drawbar.Height+(config.Visible?config.Height+3:0);
            if(y+needed>viewport.Bottom)y=selection.Top-needed-8;
            y=Math.Max(viewport.Top+3,Math.Min(y,viewport.Bottom-needed-3));
            drawbar.Location=new Point(Math.Max(0,x),Math.Max(0,y));
            config.Location=new Point(drawbar.Left,drawbar.Bottom+3);
            int ox=selection.Right+8;
            if(ox+operbar.Width>viewport.Right)ox=selection.Left-operbar.Width-8;
            ox=Math.Max(viewport.Left+3,Math.Min(ox,viewport.Right-operbar.Width-3));
            int oy=Math.Max(viewport.Top+3,Math.Min(selection.Top,viewport.Bottom-operbar.Height-3));
            operbar.Location=new Point(ox,oy);
            if(operbar.Bounds.IntersectsWith(drawbar.Bounds))operbar.Top=Math.Max(viewport.Top+3,drawbar.Top-operbar.Height-5);
            drawbar.Show();operbar.Show();drawbar.BringToFront();operbar.BringToFront();config.BringToFront();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.DrawImageUnscaled(screenshot,0,0);
            Rectangle paintRect=(!ready&&dragMode==0)?hoverRegion:selection;
            using(Region mask=new Region(ClientRectangle))
            using(Brush shade=new SolidBrush(Color.FromArgb(115,0,0,0)))
            {if(paintRect.Width>0&&paintRect.Height>0)mask.Exclude(paintRect);g.FillRegion(shade,mask);}
            if(paintRect.Width>0&&paintRect.Height>0)
            {
                GraphicsState state=g.Save();g.SetClip(selection);g.SmoothingMode=SmoothingMode.AntiAlias;
                DrawEditorAnnotations(g);
                if(pending!=null)
                {
                    if(pending.Tool==DrawTool.Blur||pending.Tool==DrawTool.Pixelate){using(Pen p=new Pen(Blue,1)){p.DashStyle=DashStyle.Dash;g.DrawRectangle(p,Annotation.Between(pending.Start,pending.End));}}
                    else pending.Draw(g);
                }
                g.Restore(state);
                using(Pen pen=new Pen(Blue,1))g.DrawRectangle(pen,paintRect.X,paintRect.Y,paintRect.Width-1,paintRect.Height-1);
                if(ready)foreach(Point p in Handles()) {g.FillRectangle(Brushes.White,p.X-3,p.Y-3,6,6);using(Pen pen=new Pen(Blue))g.DrawRectangle(pen,p.X-3,p.Y-3,6,6);}
                if(ready)DrawObjectSelection(g);
                string label=paintRect.Width+" × "+paintRect.Height;
                Size s=TextRenderer.MeasureText(label,Font);Rectangle box=new Rectangle(paintRect.Left,Math.Max(0,paintRect.Top-s.Height-9),s.Width+12,s.Height+5);
                using(Brush b=new SolidBrush(Color.FromArgb(225,36,44,54)))g.FillRectangle(b,box);
                TextRenderer.DrawText(g,label,Font,box,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            }
            if(!ready&&dragMode==0)
            {
                Rectangle screen=Screen.FromPoint(new Point(Left+pointer.X,Top+pointer.Y)).Bounds;screen.Offset(-Left,-Top);
                string hint="Trazo   ·   Clic para capturar una ventana o arrastra un área   ·   Esc para cancelar";
                Size s=TextRenderer.MeasureText(hint,Font);Rectangle r=new Rectangle(screen.Left+(screen.Width-s.Width-32)/2,screen.Top+26,s.Width+32,36);
                using(Brush b=new SolidBrush(Color.FromArgb(225,35,43,53)))g.FillRectangle(b,r);
                TextRenderer.DrawText(g,hint,Font,r,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                DrawMagnifier(g,screen);
            }
        }
        private void DrawMagnifier(Graphics g,Rectangle screen)
        {
            Point p=Clamp(pointer);int x=p.X+23,y=p.Y+23;if(x+126>screen.Right)x=p.X-145;if(y+147>screen.Bottom)y=p.Y-160;
            x=Math.Max(screen.Left,x);y=Math.Max(screen.Top,y);
            Rectangle target=new Rectangle(x,y,120,96);g.FillRectangle(Brushes.White,x-2,y-2,124,144);
            Rectangle src=Rectangle.Intersect(new Rectangle(p.X-10,p.Y-8,20,16),ClientRectangle);
            InterpolationMode old=g.InterpolationMode;g.InterpolationMode=InterpolationMode.NearestNeighbor;g.DrawImage(screenshot,target,src,GraphicsUnit.Pixel);g.InterpolationMode=old;
            using(Pen pen=new Pen(Blue)){g.DrawLine(pen,x+60,y,x+60,y+96);g.DrawLine(pen,x,y+48,x+120,y+48);g.DrawRectangle(pen,x-2,y-2,124,144);}
            Color c=screenshot.GetPixel(p.X,p.Y);string rgb=String.Format("{0}, {1}\nRGB {2}, {3}, {4}",p.X+Left,p.Y+Top,c.R,c.G,c.B);
            using(Font small=new Font("Segoe UI",8))TextRenderer.DrawText(g,rgb,small,new Rectangle(x+3,y+100,118,40),Color.FromArgb(50,60,70));
        }
        private Point Clamp(Point p){return new Point(Math.Max(0,Math.Min(screenshot.Width-1,p.X)),Math.Max(0,Math.Min(screenshot.Height-1,p.Y)));}
        private Point ClampEndpoint(Point p){return new Point(Math.Max(0,Math.Min(screenshot.Width,p.X)),Math.Max(0,Math.Min(screenshot.Height,p.Y)));}
        private Point InSelection(Point p){return new Point(Math.Max(selection.Left,Math.Min(selection.Right-1,p.X)),Math.Max(selection.Top,Math.Min(selection.Bottom-1,p.Y)));}
        private Point[] Handles(){return new Point[]{new Point(selection.Left,selection.Top),new Point(selection.Left+selection.Width/2,selection.Top),new Point(selection.Right,selection.Top),new Point(selection.Right,selection.Top+selection.Height/2),new Point(selection.Right,selection.Bottom),new Point(selection.Left+selection.Width/2,selection.Bottom),new Point(selection.Left,selection.Bottom),new Point(selection.Left,selection.Top+selection.Height/2)};}
        private int HitHandle(Point p){Point[] hs=Handles();for(int i=0;i<hs.Length;i++)if(Math.Abs(p.X-hs[i].X)<=6&&Math.Abs(p.Y-hs[i].Y)<=6)return i;return -1;}
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);CommitText();Focus();pointer=Clamp(e.Location);
            if(e.Button==MouseButtons.Right){if(!ready)Close();else ShowContext(e.Location);return;}
            if(e.Button!=MouseButtons.Left)return;
            if(ready&&tool==DrawTool.Select&&BeginObjectEdit(pointer))return;
            anchor=pointer;original=selection;
            if(ready&&(resizeHandle=HitHandle(pointer))>=0){dragMode=3;}
            else if(!ready){UpdateHoverRegion();clickCandidate=hoverRegion;selection=new Rectangle(pointer,Size.Empty);dragMode=1;}
            else if(!selection.Contains(pointer))return;
            else if(tool==DrawTool.Select){selectedId=null;dragMode=2;}
            else if(tool==DrawTool.Text){StartText(pointer);return;}
            else {pending=new Annotation();pending.Tool=tool;pending.Start=pointer;pending.End=pointer;pending.Color=ink;pending.Width=lineWidth;pending.Points.Add(pointer);dragMode=4;}
            Capture=true;drawbar.Hide();operbar.Hide();config.Hide();Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);pointer=Clamp(e.Location);
            if(dragMode==0&&!ready)UpdateHoverRegion();
            if(dragMode==1)selection=Annotation.Between(anchor,ClampEndpoint(e.Location));
            if(dragMode==2){int x=Math.Max(0,Math.Min(screenshot.Width-original.Width,original.X+pointer.X-anchor.X)),y=Math.Max(0,Math.Min(screenshot.Height-original.Height,original.Y+pointer.Y-anchor.Y));selection=new Rectangle(x,y,original.Width,original.Height);}
            if(dragMode==3)
            {
                Point end=ClampEndpoint(e.Location);int l=original.Left,t=original.Top,r=original.Right,b=original.Bottom;
                if(resizeHandle==0||resizeHandle==6||resizeHandle==7)l=Math.Min(end.X,r-4);
                if(resizeHandle==2||resizeHandle==3||resizeHandle==4)r=Math.Max(end.X,l+4);
                if(resizeHandle==0||resizeHandle==1||resizeHandle==2)t=Math.Min(end.Y,b-4);
                if(resizeHandle==4||resizeHandle==5||resizeHandle==6)b=Math.Max(end.Y,t+4);
                selection=Rectangle.FromLTRB(l,t,r,b);
            }
            if(dragMode==5||dragMode==6)UpdateObjectEdit(pointer);
            if(dragMode==4&&pending!=null)
            {
                Point end=InSelection(pointer);
                if((ModifierKeys&Keys.Shift)!=0&&(tool==DrawTool.Rect||tool==DrawTool.Ellipse))
                {int d=Math.Min(Math.Abs(end.X-anchor.X),Math.Abs(end.Y-anchor.Y));end=InSelection(new Point(anchor.X+Math.Sign(end.X-anchor.X)*d,anchor.Y+Math.Sign(end.Y-anchor.Y)*d));}
                pending.End=end;if(tool==DrawTool.Brush||tool==DrawTool.Highlight)pending.Points.Add(end);
            }
            if(dragMode==0&&ready)
            {
                int h=tool==DrawTool.Select?HitObjectHandle(pointer):-1;if(h<0)h=HitHandle(pointer);Cursor=h<0?(tool==DrawTool.Select&&selection.Contains(pointer)?Cursors.SizeAll:Cursors.Cross):((h==0||h==4)?Cursors.SizeNWSE:(h==2||h==6)?Cursors.SizeNESW:(h==1||h==5)?Cursors.SizeNS:Cursors.SizeWE);
            }
            if(dragMode!=0||!ready)Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);if(e.Button!=MouseButtons.Left||dragMode==0)return;
            Capture=false;
            if(dragMode==1){if(Math.Abs(e.X-anchor.X)<4&&Math.Abs(e.Y-anchor.Y)<4&&clickCandidate.Width>=4)selection=clickCandidate;ready=selection.Width>=4&&selection.Height>=4;}
            if(dragMode==5||dragMode==6)EndObjectEdit();
            if(dragMode==4&&pending!=null)
            {
                Annotation a=pending;pending=null;
                if(a.Tool==DrawTool.Pixelate||a.Tool==DrawTool.Blur)
                {
                    Rectangle r=Rectangle.Intersect(Annotation.Between(a.Start,a.End),selection);
                    if(r.Width>1&&r.Height>1)
                    {using(Bitmap scene=RenderScene())using(Bitmap area=scene.Clone(r,PixelFormat.Format32bppArgb)){a.Patch=a.Tool==DrawTool.Blur?Annotation.Blur(area,8):Annotation.Pixelate(area,12);}a.Start=r.Location;AddAnnotation(a);}
                    else a.Dispose();
                }
                else AddAnnotation(a);
            }
            dragMode=0;config.Visible=ready&&(tool!=DrawTool.Select||SelectedAnnotation!=null);PlaceBars();Invalidate();
        }
        private void StartText(Point point)
        {
            input=new TextBox();input.Multiline=true;input.AcceptsReturn=true;input.ForeColor=ink;input.BackColor=Color.White;input.Font=new Font("Segoe UI",textSize,FontStyle.Regular,GraphicsUnit.Pixel);
            input.Bounds=new Rectangle(point.X,point.Y,Math.Min(330,selection.Right-point.X),Math.Min(110,selection.Bottom-point.Y));input.Tag=point;
            input.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Escape){RemoveInput();e.SuppressKeyPress=true;Focus();}else if(e.KeyCode==Keys.Enter&&!e.Shift){CommitText();e.SuppressKeyPress=true;Focus();}};
            Controls.Add(input);input.BringToFront();input.Focus();
        }
        private void RemoveInput(){if(input==null)return;TextBox old=input;input=null;inputEditingId=null;Controls.Remove(old);old.Dispose();Invalidate();}
        private void CommitText()
        {
            if(input==null)return;
            if(!String.IsNullOrWhiteSpace(input.Text))
            {Annotation a=new Annotation();a.Tool=DrawTool.Text;a.Start=(Point)input.Tag;a.End=a.Start;a.Text=input.Text;a.Color=input.ForeColor;a.FontSize=input.Font.Size;if(inputEditingId.HasValue){a.Id=inputEditingId.Value;ReplaceAnnotation(a);}else AddAnnotation(a);}
            else if(inputEditingId.HasValue){selectedId=inputEditingId;DeleteSelected();}
            RemoveInput();
        }
        private void AddAnnotation(Annotation a){all.Add(a);List<Annotation> next=new List<Annotation>(history[historyIndex]);next.Add(a);selectedId=a.Id;PushHistory(next.ToArray());}
        private void PushHistory(Annotation[] values){if(historyIndex+1<history.Count)history.RemoveRange(historyIndex+1,history.Count-historyIndex-1);history.Add(values);historyIndex++;Invalidate();}
        private void Undo(){CommitText();if(historyIndex>0){historyIndex--;Invalidate();}Focus();}
        private void Redo(){if(historyIndex+1<history.Count){historyIndex++;Invalidate();}Focus();}
        private void ClearAnnotations(){if(history[historyIndex].Length>0)PushHistory(new Annotation[0]);Focus();}
        private void ResetSelection(){RemoveInput();DisposePreview();foreach(Annotation a in all)a.Dispose();all.Clear();if(pending!=null){pending.Dispose();pending=null;}selectedId=null;selection=Rectangle.Empty;ready=false;tool=DrawTool.Select;history.Clear();history.Add(new Annotation[0]);historyIndex=0;UpdateHoverRegion();PlaceBars();Cursor=Cursors.Cross;Focus();Invalidate();}
        private void FullScreen(){selection=new Rectangle(0,0,screenshot.Width,screenshot.Height);ready=true;PlaceBars();Focus();Invalidate();}
        internal Bitmap RenderScene()
        {
            Bitmap scene=screenshot.Clone(new Rectangle(0,0,screenshot.Width,screenshot.Height),PixelFormat.Format32bppArgb);
            using(Graphics g=Graphics.FromImage(scene)){g.SetClip(selection);g.SmoothingMode=SmoothingMode.AntiAlias;foreach(Annotation a in history[historyIndex])a.Draw(g);}return scene;
        }
        internal Bitmap RenderSelection(){using(Bitmap scene=RenderScene())using(Bitmap crop=scene.Clone(selection,PixelFormat.Format32bppArgb))return ImageEffects.Apply(crop,effects);}
        internal void NotifyImageExported(Bitmap image)
        {
            Action<Bitmap> handlers=ImageExported;if(handlers==null)return;
            foreach(Action<Bitmap> handler in handlers.GetInvocationList())
                try{handler(image);}catch(Exception error){System.Diagnostics.Debug.WriteLine("Trazo: "+error.Message);}
        }
        private void CopyImage()
        {
            if(!ready||exporting)return;CommitText();exporting=true;
            try{using(Bitmap image=RenderSelection()){Clipboard.SetDataObject(image,true,8,80);NotifyImageExported(image);}DialogResult=DialogResult.OK;Close();}
            catch(Exception ex){exporting=false;MessageBox.Show(this,"No se pudo copiar la captura. Intenta otra vez.\n"+ex.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        }
        private void SaveImage()
        {
            if(!ready||exporting)return;CommitText();exporting=true;
            try
            {
                using(SaveFileDialog d=new SaveFileDialog())
                {
                    d.Title="Guardar captura · Trazo";d.Filter="Imagen PNG|*.png|Imagen JPEG|*.jpg|Mapa de bits|*.bmp";d.DefaultExt="png";d.AddExtension=true;d.OverwritePrompt=true;
                    string preferred=(DefaultImageFormat??"PNG").ToUpperInvariant();d.FilterIndex=preferred=="JPEG"||preferred=="JPG"?2:preferred=="BMP"?3:1;d.DefaultExt=d.FilterIndex==2?"jpg":d.FilterIndex==3?"bmp":"png";
                    if(!String.IsNullOrWhiteSpace(DefaultSaveDirectory)){Directory.CreateDirectory(DefaultSaveDirectory);d.InitialDirectory=DefaultSaveDirectory;}
                    d.FileName="Trazo_"+DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")+"."+d.DefaultExt;
                    if(d.ShowDialog(this)!=DialogResult.OK)return;
                    string ext=Path.GetExtension(d.FileName).ToLowerInvariant();ImageFormat format=(ext==".jpg"||ext==".jpeg")?ImageFormat.Jpeg:ext==".bmp"?ImageFormat.Bmp:ImageFormat.Png;
                    using(Bitmap image=RenderSelection()){if(format==ImageFormat.Jpeg||format==ImageFormat.Bmp){using(Bitmap flat=new Bitmap(image.Width,image.Height)){using(Graphics g=Graphics.FromImage(flat)){g.Clear(Color.White);g.DrawImageUnscaled(image,0,0);}flat.Save(d.FileName,format);}}else image.Save(d.FileName,format);NotifyImageExported(image);}
                    DialogResult=DialogResult.OK;Close();
                }
            }
            catch(Exception ex){MessageBox.Show(this,"No se pudo guardar la captura.\n"+ex.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            finally{exporting=false;}
        }
        private void ShowContext(Point p)
        {
            ContextMenuStrip menu=new ContextMenuStrip();menu.Items.Add("Copiar\tCtrl+C",null,delegate{CopyImage();});menu.Items.Add("Guardar…\tCtrl+S",null,delegate{SaveImage();});menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Deshacer\tCtrl+Z",null,delegate{Undo();}).Enabled=historyIndex>0;menu.Items.Add("Rehacer\tCtrl+Y",null,delegate{Redo();}).Enabled=historyIndex+1<history.Count;
            AddEditorMenuItems(menu.Items);
            menu.Items.Add("Volver a seleccionar",null,delegate{ResetSelection();});menu.Items.Add("Cancelar\tEsc",null,delegate{Close();});menu.Closed+=delegate{menu.Dispose();};menu.Show(this,p);
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys keys)
        {
            if(keys==(Keys.Control|Keys.Shift|Keys.S)){SaveProject();return true;}
            if(keys==(Keys.Control|Keys.S)){SaveImage();return true;}
            if(input!=null)return base.ProcessCmdKey(ref msg,keys);
            if(keys==(Keys.Alt|Keys.Left)){PanCanvas(160,0);return true;}
            if(keys==(Keys.Alt|Keys.Right)){PanCanvas(-160,0);return true;}
            if(keys==(Keys.Alt|Keys.Up)){PanCanvas(0,160);return true;}
            if(keys==(Keys.Alt|Keys.Down)){PanCanvas(0,-160);return true;}
            if(keys==Keys.Escape&&SelectedAnnotation!=null){selectedId=null;config.Visible=false;Invalidate();return true;}
            if(keys==Keys.Escape){Close();return true;}
            if(keys==(Keys.Control|Keys.C)||keys==Keys.Enter){CopyImage();return true;}
            if(keys==(Keys.Control|Keys.S)){SaveImage();return true;}
            if(keys==(Keys.Control|Keys.Z)){Undo();return true;}
            if(keys==(Keys.Control|Keys.Y)||keys==(Keys.Control|Keys.Shift|Keys.Z)){Redo();return true;}
            if(keys==(Keys.Control|Keys.A)){FullScreen();return true;}
            if(keys==(Keys.Control|Keys.V)){PasteImage();return true;}
            if(keys==(Keys.Control|Keys.D)){DuplicateSelected();return true;}
            if(keys==Keys.Delete){DeleteSelected();return true;}
            if(keys==(Keys.Control|Keys.OemCloseBrackets)){ReorderSelected(1,false);return true;}
            if(keys==(Keys.Control|Keys.OemOpenBrackets)){ReorderSelected(-1,false);return true;}
            if(ready)
            {
                switch(keys){case Keys.R:SetTool(DrawTool.Rect);return true;case Keys.E:SetTool(DrawTool.Ellipse);return true;case Keys.L:SetTool(DrawTool.Line);return true;case Keys.A:SetTool(DrawTool.Arrow);return true;case Keys.P:SetTool(DrawTool.Brush);return true;case Keys.T:SetTool(DrawTool.Text);return true;case Keys.H:SetTool(DrawTool.Highlight);return true;case Keys.M:SetTool(DrawTool.Pixelate);return true;case Keys.V:SetTool(DrawTool.Select);return true;}
            }
            return base.ProcessCmdKey(ref msg,keys);
        }
        protected override void Dispose(bool disposing)
        {if(disposing){DisposePreview();foreach(Annotation a in all)a.Dispose();if(pending!=null)pending.Dispose();if(ownsScreenshot)screenshot.Dispose();tips.Dispose();}base.Dispose(disposing);}
    }

    internal sealed class GlyphButton : Control
    {
        internal readonly string Glyph;internal bool Selected;private bool hover;
        internal GlyphButton(string glyph){Glyph=glyph;Size=new Size(32,32);Cursor=Cursors.Hand;TabStop=false;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);AccessibleRole=AccessibleRole.PushButton;}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            g.Clear(Selected?Color.FromArgb(214,240,252):hover?Color.FromArgb(235,247,253):Color.White);
            if(Selected)using(Pen border=new Pen(Color.FromArgb(140,207,239)))g.DrawRectangle(border,0,0,Width-1,Height-1);
            g.TranslateTransform((Width-24)/2f,(Height-24)/2f);
            Color color=Glyph=="cancel"?Color.FromArgb(250,72,72):OverlayForm.Blue;
            using(Pen p=new Pen(color,1.7f))using(Brush b=new SolidBrush(color))
            {
                p.LineJoin=LineJoin.Round;p.StartCap=LineCap.Round;p.EndCap=LineCap.Round;
                switch(Glyph)
                {
                    case "more":g.FillEllipse(b,4,10,3,3);g.FillEllipse(b,11,10,3,3);g.FillEllipse(b,18,10,3,3);break;
                    case "rect":g.DrawRectangle(p,5,6,14,12);break;
                    case "ellipse":g.DrawEllipse(p,4,6,16,12);break;
                    case "line":g.DrawLine(p,5,19,19,5);break;
                    case "arrow":g.DrawLine(p,5,19,19,5);g.DrawLines(p,new Point[]{new Point(12,5),new Point(19,5),new Point(19,12)});break;
                    case "curve":g.DrawBezier(p,5,19,5,8,18,18,18,5);g.DrawLines(p,new Point[]{new Point(12,5),new Point(18,5),new Point(18,11)});break;
                    case "brush":g.FillPolygon(b,new Point[]{new Point(8,15),new Point(17,4),new Point(20,6),new Point(12,17)});g.FillEllipse(b,4,16,7,5);break;
                    case "highlight":g.FillPolygon(b,new Point[]{new Point(8,14),new Point(16,4),new Point(20,7),new Point(12,17)});g.DrawLines(p,new Point[]{new Point(4,20),new Point(8,18),new Point(12,20),new Point(19,20)});break;
                    case "text":using(Font f=new Font("Georgia",20,FontStyle.Bold,GraphicsUnit.Pixel))g.DrawString("A",f,b,3,1);break;
                    case "pixel":for(int y=5;y<20;y+=5)for(int x=5;x<20;x+=5)using(Brush tile=new SolidBrush((x+y)%2==0?color:Color.FromArgb(145,207,233)))g.FillRectangle(tile,x,y,4,4);break;
                    case "undo":g.DrawArc(p,7,6,12,12,195,265);g.FillPolygon(b,new Point[]{new Point(4,5),new Point(4,13),new Point(12,8)});break;
                    case "erase":g.FillPolygon(b,new Point[]{new Point(5,14),new Point(14,5),new Point(20,11),new Point(11,20)});using(Brush red=new SolidBrush(Color.FromArgb(255,106,116)))g.FillPolygon(red,new Point[]{new Point(5,14),new Point(8,11),new Point(14,17),new Point(11,20)});break;
                    case "save":g.DrawRectangle(p,6,4,13,16);g.DrawRectangle(p,9,4,6,6);g.DrawRectangle(p,9,14,7,6);break;
                    case "cancel":g.DrawEllipse(p,5,5,14,14);g.DrawLine(p,9,9,15,15);g.DrawLine(p,15,9,9,15);break;
                    case "capture":g.DrawRectangle(p,4,7,17,12);g.FillRectangle(b,8,4,6,4);using(Pen white=new Pen(Color.White,1.6f)){g.FillRectangle(b,4,7,17,12);g.DrawEllipse(white,10,10,6,6);}break;
                    case "fullscreen":g.DrawRectangle(p,4,5,16,14);g.DrawLines(p,new Point[]{new Point(7,10),new Point(7,8),new Point(10,8)});g.DrawLines(p,new Point[]{new Point(14,16),new Point(17,16),new Point(17,13)});break;
                    case "copy":g.DrawRectangle(p,9,8,11,13);g.DrawLines(p,new Point[]{new Point(16,5),new Point(5,5),new Point(5,17)});break;
                    case "select":g.DrawPolygon(p,new Point[]{new Point(6,4),new Point(6,20),new Point(11,15),new Point(16,20),new Point(19,18),new Point(14,13),new Point(21,11)});break;
                    case "palette":g.DrawEllipse(p,4,4,16,16);using(Brush red=new SolidBrush(Color.Tomato))g.FillEllipse(red,7,7,4,4);using(Brush green=new SolidBrush(Color.LimeGreen))g.FillEllipse(green,13,7,4,4);using(Brush blue=new SolidBrush(Color.DodgerBlue))g.FillEllipse(blue,7,13,4,4);break;
                    default:if(Glyph.StartsWith("dot")){int size=4+Int32.Parse(Glyph.Substring(3))*3;g.FillEllipse(b,12-size/2,12-size/2,size,size);}break;
                }
            }
        }
    }
}
