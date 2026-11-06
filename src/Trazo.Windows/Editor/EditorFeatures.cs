using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Trazo
{
    internal sealed partial class OverlayForm
    {
        private readonly CaptureWindow[] captureWindows;
        private Rectangle hoverRegion,clickCandidate;
        private Guid? selectedId,inputEditingId;
        private Annotation editPreview;
        private Rectangle objectOriginalBounds;
        private Point previousEditPoint;
        private int objectHandle=-1;
        private bool objectChanged,ownsScreenshot,syncingConfig;
        private ExportOptions effects=new ExportOptions();

        internal OverlayForm(ProjectData data) : this(CreateProjectCanvas(data),ProjectCanvasBounds(data),new CaptureWindow[0])
        {
            ownsScreenshot=true;
            Point offset=ProjectOffset(data);
            selection=new Rectangle(offset,data.Selection.Size);ready=true;
            effects=data.Effects.Clone();
            List<Annotation> values=new List<Annotation>();
            foreach(Annotation original in data.Annotations)
            {
                Annotation a=original.Clone();a.MoveBy(offset.X-data.Selection.X,offset.Y-data.Selection.Y);
                values.Add(a);all.Add(a);
            }
            history[0]=values.ToArray();PlaceBars();
        }
        private static Rectangle ProjectCanvasBounds(ProjectData data)
        {
            Rectangle screen=SystemInformation.VirtualScreen;
            return new Rectangle(screen.Location,new Size(Math.Max(screen.Width,data.Selection.Width),Math.Max(screen.Height,data.Selection.Height)));
        }
        private static Point ProjectOffset(ProjectData data)
        {
            Rectangle screen=SystemInformation.VirtualScreen;
            return new Point(Math.Max(0,(screen.Width-data.Selection.Width)/2),Math.Max(0,(screen.Height-data.Selection.Height)/2));
        }
        private static Bitmap CreateProjectCanvas(ProjectData data)
        {
            Rectangle bounds=ProjectCanvasBounds(data);Bitmap canvas=new Bitmap(bounds.Width,bounds.Height,PixelFormat.Format32bppArgb);
            using(Graphics g=Graphics.FromImage(canvas))
            {g.Clear(Color.FromArgb(43,50,60));g.DrawImage(data.Screenshot,new Rectangle(ProjectOffset(data),data.Selection.Size),data.Selection,GraphicsUnit.Pixel);}
            return canvas;
        }
        private Annotation SelectedAnnotation
        {
            get{if(selectedId.HasValue)foreach(Annotation a in history[historyIndex])if(a.Id==selectedId.Value)return a;return null;}
        }
        private void UpdateHoverRegion()
        {
            if(ready)return;
            if(captureWindows.Length==0){hoverRegion=Rectangle.Empty;return;}
            Rectangle r=WindowDetection.FindAt(captureWindows,new Point(Left+pointer.X,Top+pointer.Y),new Rectangle(Location,screenshot.Size));
            r.Offset(-Left,-Top);hoverRegion=Rectangle.Intersect(new Rectangle(Point.Empty,screenshot.Size),r);
        }
        private static Point[] ObjectHandles(Rectangle r)
        {return new Point[]{new Point(r.Left,r.Top),new Point(r.Left+r.Width/2,r.Top),new Point(r.Right,r.Top),new Point(r.Right,r.Top+r.Height/2),new Point(r.Right,r.Bottom),new Point(r.Left+r.Width/2,r.Bottom),new Point(r.Left,r.Bottom),new Point(r.Left,r.Top+r.Height/2)};}
        private int HitObjectHandle(Point p)
        {
            Annotation a=SelectedAnnotation;if(a==null)return -1;
            Point[] hs=ObjectHandles(a.GetBounds());for(int i=0;i<hs.Length;i++)if(Math.Abs(p.X-hs[i].X)<=6&&Math.Abs(p.Y-hs[i].Y)<=6)return i;
            return -1;
        }
        private Annotation HitAnnotation(Point point)
        {
            Annotation[] values=history[historyIndex];
            for(int i=values.Length-1;i>=0;i--)if(values[i].HitTest(point,5))return values[i];return null;
        }
        private bool BeginObjectEdit(Point point)
        {
            if(!selection.Contains(point))return false;
            objectHandle=HitObjectHandle(point);Annotation a=SelectedAnnotation;
            if(objectHandle<0)a=HitAnnotation(point);
            if(a==null){selectedId=null;Invalidate();return false;}
            selectedId=a.Id;ink=a.Color;lineWidth=a.Width;textSize=a.FontSize;SyncConfig();
            DisposePreview();editPreview=a.Clone();objectOriginalBounds=a.GetBounds();previousEditPoint=point;anchor=point;objectChanged=false;
            dragMode=objectHandle>=0?6:5;Capture=true;drawbar.Hide();operbar.Hide();config.Hide();Invalidate();return true;
        }
        private void UpdateObjectEdit(Point point)
        {
            if(editPreview==null)return;
            if(point==previousEditPoint)return;
            if(dragMode==5)
            {
                int dx=point.X-previousEditPoint.X,dy=point.Y-previousEditPoint.Y;
                Rectangle b=editPreview.GetBounds();
                if(b.Width<=selection.Width)dx=Math.Max(selection.Left-b.Left,Math.Min(selection.Right-b.Right,dx));
                if(b.Height<=selection.Height)dy=Math.Max(selection.Top-b.Top,Math.Min(selection.Bottom-b.Bottom,dy));
                editPreview.MoveBy(dx,dy);objectChanged|=dx!=0||dy!=0;
            }
            else
            {
                int l=objectOriginalBounds.Left,t=objectOriginalBounds.Top,r=objectOriginalBounds.Right,b=objectOriginalBounds.Bottom;
                int x=Math.Max(selection.Left,Math.Min(selection.Right,point.X)),y=Math.Max(selection.Top,Math.Min(selection.Bottom,point.Y));
                if(objectHandle==0||objectHandle==6||objectHandle==7)l=Math.Min(x,r-6);
                if(objectHandle==2||objectHandle==3||objectHandle==4)r=Math.Max(x,l+6);
                if(objectHandle==0||objectHandle==1||objectHandle==2)t=Math.Min(y,b-6);
                if(objectHandle==4||objectHandle==5||objectHandle==6)b=Math.Max(y,t+6);
                Annotation originalAnnotation=SelectedAnnotation;
                if(originalAnnotation!=null&&originalAnnotation.Patch==null){editPreview.Dispose();editPreview=originalAnnotation.Clone();}
                editPreview.ResizeTo(Rectangle.FromLTRB(l,t,r,b));
                if(originalAnnotation!=null&&originalAnnotation.Tool==DrawTool.Text&&(objectHandle==1||objectHandle==3||objectHandle==5||objectHandle==7))
                {
                    float factor=(objectHandle==3||objectHandle==7)?(r-l)/(float)Math.Max(1,objectOriginalBounds.Width):(b-t)/(float)Math.Max(1,objectOriginalBounds.Height);
                    editPreview.FontSize=Math.Max(6,Math.Min(512,originalAnnotation.FontSize*factor));
                }
                objectChanged=true;
            }
            previousEditPoint=point;Invalidate();
        }
        private void EndObjectEdit()
        {
            if(editPreview==null)return;
            Annotation a=editPreview;editPreview=null;
            if(objectChanged)ReplaceAnnotation(a);else a.Dispose();
        }
        private void DisposePreview(){if(editPreview!=null){editPreview.Dispose();editPreview=null;}}
        private void DrawEditorAnnotations(Graphics g)
        {
            foreach(Annotation a in history[historyIndex])
            {
                if(inputEditingId.HasValue&&a.Id==inputEditingId.Value)continue;
                if(editPreview!=null&&a.Id==editPreview.Id)editPreview.Draw(g);else a.Draw(g);
            }
        }
        private void DrawObjectSelection(Graphics g)
        {
            if(tool!=DrawTool.Select)return;
            Annotation a=editPreview??SelectedAnnotation;if(a==null)return;
            Rectangle r=a.GetBounds();GraphicsState state=g.Save();g.SetClip(selection);
            using(Pen pen=new Pen(Blue,1))
            {
                pen.DashStyle=DashStyle.Dash;g.DrawRectangle(pen,r);
                pen.DashStyle=DashStyle.Solid;
                foreach(Point point in ObjectHandles(r)){g.FillRectangle(Brushes.White,point.X-3,point.Y-3,6,6);g.DrawRectangle(pen,point.X-3,point.Y-3,6,6);}
            }
            g.Restore(state);
        }
        private void ReplaceAnnotation(Annotation changed)
        {
            Annotation[] next=(Annotation[])history[historyIndex].Clone();
            for(int i=0;i<next.Length;i++)if(next[i].Id==changed.Id){next[i]=changed;all.Add(changed);selectedId=changed.Id;PushHistory(next);return;}
            changed.Dispose();
        }
        private void ApplySelectedStyle(bool color,bool width,bool font)
        {
            Annotation current=SelectedAnnotation;if(current==null)return;
            if(current.Patch!=null)return;
            Annotation next=current.Clone();if(color)next.Color=ink;if(width)next.Width=lineWidth;if(font&&next.Tool==DrawTool.Text)next.FontSize=textSize;ReplaceAnnotation(next);
        }
        private void SyncConfig()
        {
            syncingConfig=true;
            try
            {
                foreach(Control control in config.Controls)
                {
                    ComboBox combo=control as ComboBox;
                    if(combo!=null)
                    {
                        object match=null;foreach(object value in combo.Items)if(Math.Abs(Convert.ToSingle(value)-textSize)<.1f){match=value;break;}
                        if(match==null){match=(float)Math.Round(textSize,1);combo.Items.Add(match);}combo.SelectedItem=match;
                    }
                    GlyphButton button=control as GlyphButton;
                    if(button!=null&&button.Glyph.StartsWith("dot")){int index=Int32.Parse(button.Glyph.Substring(3));button.Selected=Math.Abs(lineWidth-new float[]{2,4,7}[index])<.1;}
                }
                config.Invalidate(true);
            }
            finally{syncingConfig=false;}
        }
        private void DuplicateSelected()
        {
            CommitText();Annotation a=SelectedAnnotation;if(a==null)return;Annotation copy=a.Duplicate();
            Rectangle b=copy.GetBounds();if(b.Width<=selection.Width&&b.Height<=selection.Height)copy.MoveBy(Math.Max(selection.Left-b.Left,Math.Min(0,selection.Right-b.Right)),Math.Max(selection.Top-b.Top,Math.Min(0,selection.Bottom-b.Bottom)));
            AddAnnotation(copy);ActivateSelectionTool();config.Visible=true;PlaceBars();Focus();
        }
        private void DeleteSelected()
        {
            Annotation a=SelectedAnnotation;if(a==null)return;
            List<Annotation> next=new List<Annotation>(history[historyIndex]);next.Remove(a);PushHistory(next.ToArray());Focus();
        }
        private void ReorderSelected(int direction,bool extreme)
        {
            CommitText();Annotation a=SelectedAnnotation;if(a==null)return;
            List<Annotation> next=new List<Annotation>(history[historyIndex]);int index=next.IndexOf(a);
            int target=extreme?(direction>0?next.Count-1:0):Math.Max(0,Math.Min(next.Count-1,index+direction));
            if(target==index)return;next.RemoveAt(index);next.Insert(target,a);PushHistory(next.ToArray());Focus();
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if(e.Button!=MouseButtons.Left||!ready||tool!=DrawTool.Select)return;
            Annotation a=HitAnnotation(e.Location);if(a==null||a.Tool!=DrawTool.Text)return;
            CommitText();selectedId=a.Id;ink=a.Color;textSize=a.FontSize;StartText(a.Start);
            inputEditingId=a.Id;input.Text=a.Text??"";input.SelectAll();Invalidate();
        }
        private void PasteImage()
        {
            if(!ready)return;CommitText();
            try{if(!Clipboard.ContainsImage()){MessageBox.Show(this,"Copia una imagen al portapapeles y vuelve a pegarla.","Trazo",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}using(Image image=Clipboard.GetImage())if(image!=null)InsertImage(image);}
            catch(Exception e){MessageBox.Show(this,"No se pudo pegar la imagen.\n"+e.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        }
        private void ImportImage()
        {
            if(!ready)return;CommitText();
            using(OpenFileDialog dialog=new OpenFileDialog())
            {
                dialog.Title="Añadir imagen a la captura";dialog.Filter="Imágenes|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Todos los archivos|*.*";
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                try{using(Image image=Image.FromFile(dialog.FileName))InsertImage(image);}
                catch(Exception e){MessageBox.Show(this,"No se pudo abrir la imagen.\n"+e.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            }
        }
        private void InsertImage(Image image)
        {
            double scale=Math.Min(1,Math.Min(selection.Width/(double)image.Width,selection.Height/(double)image.Height));
            int w=Math.Max(1,(int)Math.Round(image.Width*scale)),h=Math.Max(1,(int)Math.Round(image.Height*scale));
            Annotation a=new Annotation();a.Tool=DrawTool.Image;a.Patch=new Bitmap(image);a.Start=new Point(selection.X+(selection.Width-w)/2,selection.Y+(selection.Height-h)/2);a.End=new Point(a.Start.X+w,a.Start.Y+h);
            AddAnnotation(a);ActivateSelectionTool();config.Visible=true;PlaceBars();Focus();
        }
        private void ActivateSelectionTool(){tool=DrawTool.Select;foreach(GlyphButton button in tools){button.Selected=(DrawTool)button.Tag==DrawTool.Select;button.Invalidate();}}
        private void SaveProject()
        {
            if(!ready||exporting)return;CommitText();
            using(SaveFileDialog dialog=new SaveFileDialog())
            {
                dialog.Title="Guardar proyecto editable · Trazo";dialog.Filter="Proyecto de Trazo|*.trazo";dialog.DefaultExt="trazo";dialog.AddExtension=true;dialog.OverwritePrompt=true;dialog.FileName="Trazo_"+DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")+".trazo";
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                try{WriteProject(dialog.FileName);}
                catch(Exception e){MessageBox.Show(this,"No se pudo guardar el proyecto.\n"+e.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            }
            Focus();
        }
        internal void WriteProject(string path)
        {
            List<Annotation> values=new List<Annotation>();
            try
            {
                foreach(Annotation original in history[historyIndex])if(original.GetBounds().IntersectsWith(selection))
                {
                    Annotation a=original.Clone();
                    if(a.Patch!=null)
                    {
                        Rectangle destination=original.GetBounds(),visible=Rectangle.Intersect(destination,selection);
                        Bitmap clipped=new Bitmap(visible.Width,visible.Height,PixelFormat.Format32bppArgb);
                        using(Graphics g=Graphics.FromImage(clipped)){g.Clear(Color.Transparent);g.DrawImage(original.Patch,new Rectangle(destination.X-visible.X,destination.Y-visible.Y,destination.Width,destination.Height),0,0,original.Patch.Width,original.Patch.Height,GraphicsUnit.Pixel);}
                        a.Patch.Dispose();a.Patch=clipped;a.Start=visible.Location;a.End=new Point(visible.Right,visible.Bottom);
                    }
                    a.MoveBy(-selection.X,-selection.Y);values.Add(a);
                }
                using(Bitmap crop=screenshot.Clone(selection,PixelFormat.Format32bppArgb))
                    ProjectStore.Save(path,crop,new Rectangle(Left+selection.Left,Top+selection.Top,selection.Width,selection.Height),new Rectangle(Point.Empty,selection.Size),values.ToArray(),effects);
            }
            finally{foreach(Annotation a in values)a.Dispose();}
        }
        private void ShowEditorMenu()
        {
            ContextMenuStrip menu=new ContextMenuStrip();AddEditorMenuItems(menu.Items);menu.Closed+=delegate{menu.Dispose();};menu.Show(operbar,new Point(-menu.PreferredSize.Width,0));
        }
        private void PanCanvas(int dx,int dy)
        {
            Rectangle desktop=SystemInformation.VirtualScreen;
            int minX=desktop.Right-Math.Max(desktop.Width,screenshot.Width),minY=desktop.Bottom-Math.Max(desktop.Height,screenshot.Height);
            Location=new Point(Math.Max(minX,Math.Min(desktop.Left,Left+dx)),Math.Max(minY,Math.Min(desktop.Top,Top+dy)));
            PlaceBars();Focus();Invalidate();
        }
        private void AddEditorMenuItems(ToolStripItemCollection items)
        {
            bool selected=SelectedAnnotation!=null;
            items.Add(new ToolStripSeparator());
            items.Add("Seleccionar y editar marcas\tV",null,delegate{SetTool(DrawTool.Select);});
            items.Add("Duplicar marca\tCtrl+D",null,delegate{DuplicateSelected();}).Enabled=selected;
            items.Add("Eliminar marca\tSupr",null,delegate{DeleteSelected();}).Enabled=selected;
            ToolStripMenuItem layers=new ToolStripMenuItem("Orden de la marca");layers.Enabled=selected;
            layers.DropDownItems.Add("Traer al frente",null,delegate{ReorderSelected(1,true);});layers.DropDownItems.Add("Subir una capa\tCtrl+]",null,delegate{ReorderSelected(1,false);});layers.DropDownItems.Add("Bajar una capa\tCtrl+[",null,delegate{ReorderSelected(-1,false);});layers.DropDownItems.Add("Enviar al fondo",null,delegate{ReorderSelected(-1,true);});items.Add(layers);
            items.Add(new ToolStripSeparator());items.Add("Pegar imagen\tCtrl+V",null,delegate{PasteImage();});items.Add("Añadir imagen desde archivo…",null,delegate{ImportImage();});
            items.Add("Guardar proyecto editable…\tCtrl+Mayús+S",null,delegate{SaveProject();});
            if(screenshot.Width>SystemInformation.VirtualScreen.Width||screenshot.Height>SystemInformation.VirtualScreen.Height)
            {
                ToolStripMenuItem view=new ToolStripMenuItem("Desplazar vista del proyecto");
                view.DropDownItems.Add("Ver a la izquierda\tAlt+←",null,delegate{PanCanvas(160,0);});view.DropDownItems.Add("Ver a la derecha\tAlt+→",null,delegate{PanCanvas(-160,0);});view.DropDownItems.Add("Ver arriba\tAlt+↑",null,delegate{PanCanvas(0,160);});view.DropDownItems.Add("Ver abajo\tAlt+↓",null,delegate{PanCanvas(0,-160);});items.Add(view);
            }
            ToolStripMenuItem effectMenu=new ToolStripMenuItem("Efectos de la captura");
            ToolStripMenuItem gray=new ToolStripMenuItem("Escala de grises");gray.Checked=effects.Grayscale;gray.Click+=delegate{effects.Grayscale=!effects.Grayscale;ShowEffectsPreview();};effectMenu.DropDownItems.Add(gray);
            ToolStripMenuItem border=new ToolStripMenuItem("Borde blanco");border.Checked=effects.Border;border.Click+=delegate{effects.Border=!effects.Border;ShowEffectsPreview();};effectMenu.DropDownItems.Add(border);
            ToolStripMenuItem shadow=new ToolStripMenuItem("Sombra suave");shadow.Checked=effects.Shadow;shadow.Click+=delegate{effects.Shadow=!effects.Shadow;ShowEffectsPreview();};effectMenu.DropDownItems.Add(shadow);
            effectMenu.DropDownItems.Add("Girar 90° a la derecha",null,delegate{effects.QuarterTurns=(effects.QuarterTurns+1)%4;ShowEffectsPreview();});
            effectMenu.DropDownItems.Add("Restablecer efectos",null,delegate{effects=new ExportOptions();ShowEffectsPreview();});items.Add(effectMenu);
            items.Add("Vista previa de la captura…",null,delegate{ShowEffectsPreview();});
        }
        private void ShowEffectsPreview()
        {
            if(!ready)return;CommitText();
            using(Bitmap image=RenderSelection())using(Form dialog=new Form())
            {
                dialog.Text="Trazo · Vista previa · "+image.Width+" × "+image.Height;dialog.StartPosition=FormStartPosition.CenterParent;dialog.Size=new Size(Math.Min(950,Math.Max(400,image.Width+40)),Math.Min(700,Math.Max(260,image.Height+100)));dialog.BackColor=Color.FromArgb(235,239,243);dialog.TopMost=true;
                PictureBox picture=new PictureBox();picture.Dock=DockStyle.Fill;picture.SizeMode=PictureBoxSizeMode.Zoom;picture.Image=image;dialog.Controls.Add(picture);
                Button close=new Button();close.Dock=DockStyle.Bottom;close.Height=40;close.Text="Volver al editor";close.BackColor=Color.White;close.Click+=delegate{dialog.Close();};dialog.Controls.Add(close);dialog.AcceptButton=close;dialog.CancelButton=close;dialog.ShowDialog(this);
            }
            Focus();
        }
    }
}
