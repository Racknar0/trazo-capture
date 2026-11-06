using System;
using System.Drawing;
using System.Windows.Forms;
namespace Trazo
{
    internal sealed class ClosePromptForm:Form
    {
        private readonly CheckBox remember;
        internal CloseBehavior ResultBehavior=CloseBehavior.Ask;
        internal bool RememberChoice {get{return remember.Checked;}}
        internal ClosePromptForm()
        {
            Text="Cerrar Trazo";StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
            ClientSize=new Size(510,240);AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;BackColor=Color.White;Font=new Font("Segoe UI",10);
            Panel header=new Panel{Dock=DockStyle.Top,Height=48,BackColor=Color.FromArgb(0,163,255)};header.Controls.Add(new Label{Text="¿Qué quieres hacer con Trazo?",Dock=DockStyle.Fill,ForeColor=Color.White,Font=new Font("Segoe UI",13),Padding=new Padding(20,0,0,0),TextAlign=ContentAlignment.MiddleLeft});Controls.Add(header);
            Label explanation=new Label{Text="En la bandeja seguirá disponible para capturar con tus atajos. Si lo cierras por completo, dejarán de funcionar hasta volver a abrirlo.",Bounds=new Rectangle(22,68,466,57),ForeColor=Color.FromArgb(90,101,113)};Controls.Add(explanation);
            remember=new CheckBox{Text="Recordar mi elección",AutoSize=true,Location=new Point(24,137)};Controls.Add(remember);
            Button tray=new Button{Text="Dejar en la bandeja",Bounds=new Rectangle(22,183,187,34),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(0,163,255),ForeColor=Color.White};tray.FlatAppearance.BorderSize=0;tray.Click+=delegate{ResultBehavior=CloseBehavior.Tray;DialogResult=DialogResult.OK;Close();};Controls.Add(tray);
            Button exit=new Button{Text="Cerrar Trazo",Bounds=new Rectangle(221,183,132,34)};exit.Click+=delegate{ResultBehavior=CloseBehavior.Exit;DialogResult=DialogResult.OK;Close();};Controls.Add(exit);
            Button cancel=new Button{Text="Cancelar",Bounds=new Rectangle(366,183,122,34),DialogResult=DialogResult.Cancel};Controls.Add(cancel);CancelButton=cancel;
        }
    }
}
