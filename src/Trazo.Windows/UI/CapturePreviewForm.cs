using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
namespace Trazo
{
    internal sealed class CapturePreviewForm:Form
    {
        private readonly Bitmap image;
        internal CapturePreviewForm(CaptureRecord record)
        {
            using(Image source=Image.FromFile(record.FullPath))image=new Bitmap(source);
            Text="Trazo · "+record.CreatedUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm")+" · "+image.Width+" × "+image.Height;
            StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(860,590);MinimumSize=new Size(520,380);BackColor=Color.FromArgb(235,239,244);Font=new Font("Segoe UI",10);
            FlowLayoutPanel toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=52,Padding=new Padding(12,8,12,8),BackColor=Color.White,WrapContents=false};
            Button edit=ActionButton("Editar");edit.Click+=delegate{DialogResult=DialogResult.Yes;Close();};toolbar.Controls.Add(edit);
            Button copy=ActionButton("Copiar");copy.Click+=delegate{try{Clipboard.SetDataObject(image,true,8,80);}catch(Exception error){MessageBox.Show(this,error.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};toolbar.Controls.Add(copy);
            Button save=ActionButton("Guardar como…");save.Width=145;save.Click+=delegate{using(SaveFileDialog dialog=new SaveFileDialog{Filter="Imagen PNG|*.png|Imagen JPEG|*.jpg|Mapa de bits|*.bmp",FileName="Trazo_"+record.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss")+".png",AddExtension=true,DefaultExt="png",OverwritePrompt=true}){if(dialog.ShowDialog(this)==DialogResult.OK){try{WriteImage(image,dialog.FileName);}catch(Exception error){MessageBox.Show(this,error.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}}};toolbar.Controls.Add(save);
            PictureBox picture=new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,Image=image};Controls.Add(picture);Controls.Add(toolbar);
        }
        private static Button ActionButton(string text){return new Button{Text=text,Size=new Size(102,34),FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=Color.FromArgb(45,60,75),Margin=new Padding(0,0,8,0)};}
        internal static void WriteImage(Bitmap image,string path)
        {
            string extension=Path.GetExtension(path).ToLowerInvariant();ImageFormat format=extension==".jpg"||extension==".jpeg"?ImageFormat.Jpeg:extension==".bmp"?ImageFormat.Bmp:ImageFormat.Png;
            if(format==ImageFormat.Png){image.Save(path,format);return;}
            using(Bitmap flat=new Bitmap(image.Width,image.Height)){using(Graphics g=Graphics.FromImage(flat)){g.Clear(Color.White);g.DrawImageUnscaled(image,0,0);}flat.Save(path,format);}
        }
        protected override void Dispose(bool disposing){if(disposing)image.Dispose();base.Dispose(disposing);}
    }
}
