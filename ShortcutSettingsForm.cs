using System;
using System.Drawing;
using System.Windows.Forms;

namespace Trazo
{
    internal sealed class ShortcutSettingsForm : Form
    {
        private readonly ListBox list;
        private readonly TextBox keyInput;
        private readonly Label status;
        private readonly ShortcutSettings draft;
        private Keys pending=Keys.None;
        internal ShortcutSettings ResultSettings;

        internal ShortcutSettingsForm(ShortcutSettings settings)
        {
            draft=settings.Clone();Text="Trazo · Atajos de captura";StartPosition=FormStartPosition.CenterParent;
            FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
            ClientSize=new Size(478,414);AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;Font=new Font("Segoe UI",9.5f);BackColor=Color.White;
            Label heading=new Label{Text="Atajos de captura",Font=new Font("Segoe UI",16),AutoSize=true,Location=new Point(20,15)};Controls.Add(heading);
            Label help=new Label{Text="Puedes añadir, cambiar o quitar los atajos que inician una captura.",Bounds=new Rectangle(20,53,440,36),ForeColor=Color.DimGray};Controls.Add(help);
            list=new ListBox{Bounds=new Rectangle(20,94,300,133),IntegralHeight=false};Controls.Add(list);
            Button remove=new Button{Text="Quitar",Bounds=new Rectangle(334,94,124,32)};remove.Click+=delegate{if(list.SelectedIndex>=0){draft.Shortcuts.RemoveAt(list.SelectedIndex);RefreshList();}};Controls.Add(remove);
            Button reset=new Button{Text="Restablecer",Bounds=new Rectangle(334,136,124,32)};reset.Click+=delegate{draft.Shortcuts.Clear();draft.Shortcuts.AddRange(ShortcutSettings.CreateDefault().Shortcuts);RefreshList();};Controls.Add(reset);
            Label prompt=new Label{Text="Pulsa aquí la combinación que quieras usar:",AutoSize=true,Location=new Point(20,241)};Controls.Add(prompt);
            keyInput=new TextBox{Bounds=new Rectangle(20,267,300,30),ReadOnly=true,BackColor=Color.FromArgb(246,249,252),Text="Haz clic y pulsa el atajo",AccessibleName="Nueva combinación de teclas"};Controls.Add(keyInput);
            keyInput.PreviewKeyDown+=delegate(object sender,PreviewKeyDownEventArgs e){e.IsInputKey=true;};
            keyInput.KeyDown+=delegate(object sender,KeyEventArgs e){CaptureCombination(e.KeyData);e.SuppressKeyPress=true;};
            keyInput.KeyUp+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.PrintScreen){CaptureCombination(e.KeyData);e.SuppressKeyPress=true;}};
            Button add=new Button{Text="Añadir",Bounds=new Rectangle(334,264,124,32)};add.Click+=delegate{AddPending(false);};Controls.Add(add);
            Button change=new Button{Text="Cambiar seleccionado",Bounds=new Rectangle(20,306,185,30)};change.Click+=delegate{AddPending(true);};Controls.Add(change);
            status=new Label{Text="Puedes usar una tecla o una combinación. Windows reserva algunos atajos.",Bounds=new Rectangle(20,342,438,30),ForeColor=Color.DimGray,Font=new Font("Segoe UI",8.5f)};Controls.Add(status);
            Button save=new Button{Text="Guardar",Bounds=new Rectangle(251,374,98,30),BackColor=Color.FromArgb(0,163,255),ForeColor=Color.White,FlatStyle=FlatStyle.Flat};save.FlatAppearance.BorderSize=0;
            save.Click+=delegate
            {
                try{draft.Save();ResultSettings=draft.Clone();DialogResult=DialogResult.OK;Close();}
                catch(Exception error){MessageBox.Show(this,"No se pudieron guardar los atajos.\n"+error.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            };Controls.Add(save);
            Button cancel=new Button{Text="Cancelar",Bounds=new Rectangle(360,374,98,30),DialogResult=DialogResult.Cancel};Controls.Add(cancel);CancelButton=cancel;
            RefreshList();
        }
        protected override bool ProcessCmdKey(ref Message message,Keys keyData)
        {
            if(keyInput.Focused){CaptureCombination(keyData);return true;}return base.ProcessCmdKey(ref message,keyData);
        }
        private void CaptureCombination(Keys keys)
        {
            Keys key=keys&Keys.KeyCode;
            if(key==Keys.ControlKey||key==Keys.ShiftKey||key==Keys.Menu||key==Keys.LControlKey||key==Keys.RControlKey||key==Keys.LShiftKey||key==Keys.RShiftKey||key==Keys.LMenu||key==Keys.RMenu||key==Keys.LWin||key==Keys.RWin)return;
            if(Native.HasWindowsModifier())keys|=ShortcutSettings.WindowsModifier;
            pending=keys;keyInput.Text=ShortcutSettings.Format(keys);
            status.Text=ShortcutSettings.IsValid(keys)?"Pulsa Añadir o Cambiar seleccionado para usar esta combinación.":"Pulsa una tecla de teclado, con o sin modificadores.";
        }
        private void AddPending(bool replace)
        {
            if(!ShortcutSettings.IsValid(pending)){status.Text="Primero pulsa una combinación válida en el cuadro de teclas.";keyInput.Focus();return;}
            int index=replace?list.SelectedIndex:-1;
            if(replace&&index<0){status.Text="Selecciona en la lista el atajo que quieres cambiar.";return;}
            int existing=draft.Shortcuts.IndexOf(pending);
            if(existing>=0&&existing!=index){status.Text="Esa combinación ya está en la lista.";return;}
            if(index>=0)draft.Shortcuts[index]=pending;
            else {if(draft.Shortcuts.Count>=ShortcutSettings.MaxShortcuts){status.Text="Puedes añadir hasta "+ShortcutSettings.MaxShortcuts+" atajos.";return;}draft.Shortcuts.Add(pending);}
            RefreshList();status.Text="Los cambios se aplicarán al guardar.";
        }
        private void RefreshList()
        {
            list.Items.Clear();foreach(Keys shortcut in draft.Shortcuts)list.Items.Add(ShortcutSettings.Format(shortcut));
            if(list.Items.Count>0)list.SelectedIndex=0;
        }
    }
}
