using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
namespace Trazo
{
    internal sealed class SettingsForm:Form
    {
        private readonly AppSettings draft;
        private readonly bool previewMode;
        protected override bool ShowWithoutActivation {get{return previewMode;}}
        [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr first,IntPtr second);
        private readonly Panel pages;
        private readonly ListBox navigation;
        private readonly Panel[] sections;
        private readonly CheckBox startup,minimized,historyEnabled,hideMain;
        private readonly ComboBox closeBehavior,format;
        private readonly NumericUpDown historyLimit;
        private readonly TextBox saveDirectory;
        internal AppSettings ResultSettings;
        internal SettingsForm(AppSettings settings,string historyDirectory,bool startupEnabled,Action configureShortcuts,bool previewMode=false)
        {
            this.previewMode=previewMode;draft=settings.Clone();Text="Trazo · Configuración";StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.None;
            MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;ClientSize=new Size(760,555);AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;Font=new Font("Segoe UI",10);BackColor=Color.White;
            Panel title=new Panel{Dock=DockStyle.Top,Height=43,BackColor=Color.FromArgb(0,163,255)};Label titleText=new Label{Text="Configuración",Dock=DockStyle.Fill,ForeColor=Color.White,Font=new Font("Segoe UI",12),Padding=new Padding(20,0,0,0),TextAlign=ContentAlignment.MiddleLeft};title.Controls.Add(titleText);
            Button titleClose=new Button{Text="×",Dock=DockStyle.Right,Width=45,FlatStyle=FlatStyle.Flat,BackColor=title.BackColor,ForeColor=Color.White,Font=new Font("Segoe UI",16),TabStop=false};titleClose.FlatAppearance.BorderSize=0;titleClose.FlatAppearance.MouseOverBackColor=Color.FromArgb(232,68,78);titleClose.Click+=delegate{DialogResult=DialogResult.Cancel;Close();};title.Controls.Add(titleClose);titleClose.BringToFront();
            TableLayoutPanel titleLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,Padding=Padding.Empty};titleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));titleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,45));titleLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));title.Controls.Clear();titleText.Margin=titleClose.Margin=Padding.Empty;titleClose.Dock=DockStyle.Fill;titleLayout.Controls.Add(titleText,0,0);titleLayout.Controls.Add(titleClose,1,0);title.Controls.Add(titleLayout);
            MouseEventHandler move=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0x00A1,new IntPtr(2),IntPtr.Zero);}};title.MouseDown+=move;titleText.MouseDown+=move;
            Panel footer=new Panel{Dock=DockStyle.Bottom,Width=ClientSize.Width,Height=58,BackColor=Color.FromArgb(245,246,249)};
            Button save=new Button{Text="Guardar",Size=new Size(104,32),Location=new Point(525,13),Anchor=AnchorStyles.Top|AnchorStyles.Right,BackColor=Color.FromArgb(0,163,255),ForeColor=Color.White,FlatStyle=FlatStyle.Flat};save.FlatAppearance.BorderSize=0;save.Click+=delegate{SaveChanges();};footer.Controls.Add(save);
            Button cancel=new Button{Text="Cancelar",Size=new Size(104,32),Location=new Point(640,13),Anchor=AnchorStyles.Top|AnchorStyles.Right,DialogResult=DialogResult.Cancel};footer.Controls.Add(cancel);CancelButton=cancel;
            navigation=new ListBox{Dock=DockStyle.Left,Width=150,BorderStyle=BorderStyle.None,BackColor=Color.FromArgb(249,250,252),DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=48,IntegralHeight=false};navigation.Items.AddRange(new object[]{"General","Capturas","Historial","Atajos"});navigation.DrawItem+=DrawNavigation;
            pages=new Panel{Dock=DockStyle.Fill,BackColor=Color.White};sections=new Panel[4];for(int i=0;i<sections.Length;i++){sections[i]=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Visible=false,Padding=new Padding(28)};pages.Controls.Add(sections[i]);}
            Controls.Add(pages);Controls.Add(navigation);Controls.Add(footer);Controls.Add(title);
            SectionTitle(sections[0],"General");
            startup=new CheckBox{Text="Iniciar Trazo con Windows",Checked=startupEnabled,AutoSize=true,Location=new Point(28,72)};sections[0].Controls.Add(startup);
            minimized=new CheckBox{Text="Iniciar en la bandeja",Checked=draft.StartMinimized,AutoSize=true,Location=new Point(28,111)};sections[0].Controls.Add(minimized);
            sections[0].Controls.Add(Description("Al iniciar con Windows, la ventana puede permanecer oculta junto al reloj.",28,146,470,48));
            sections[0].Controls.Add(Label("Al cerrar la ventana",28,225));closeBehavior=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Bounds=new Rectangle(28,255,420,30)};closeBehavior.Items.AddRange(new object[]{"Preguntar cada vez","Dejar en la bandeja","Cerrar Trazo por completo"});closeBehavior.SelectedIndex=(int)draft.CloseBehavior;sections[0].Controls.Add(closeBehavior);
            sections[0].Controls.Add(Description("Puedes volver a cambiar aquí la elección que recuerde el aviso de cierre.",28,304,470,52));
            SectionTitle(sections[1],"Capturas");sections[1].Controls.Add(Label("Carpeta sugerida al guardar",28,73));
            saveDirectory=new TextBox{Text=draft.SaveDirectory,Bounds=new Rectangle(28,105,406,29)};sections[1].Controls.Add(saveDirectory);
            Button browse=new Button{Text="…",Bounds=new Rectangle(444,103,51,31)};browse.Click+=delegate{using(FolderBrowserDialog dialog=new FolderBrowserDialog()){dialog.Description="Carpeta para guardar tus capturas";if(Directory.Exists(saveDirectory.Text))dialog.SelectedPath=saveDirectory.Text;if(dialog.ShowDialog(this)==DialogResult.OK)saveDirectory.Text=dialog.SelectedPath;}};sections[1].Controls.Add(browse);
            sections[1].Controls.Add(Label("Formato de imagen predeterminado",28,164));format=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Bounds=new Rectangle(28,196,250,30)};format.Items.AddRange(new object[]{"PNG","JPEG","BMP"});format.SelectedItem=draft.ImageFormat;if(format.SelectedIndex<0)format.SelectedIndex=0;sections[1].Controls.Add(format);
            hideMain=new CheckBox{Text="Ocultar la ventana principal al capturar",Checked=draft.HideMainDuringCapture,AutoSize=true,Location=new Point(28,266)};sections[1].Controls.Add(hideMain);
            sections[1].Controls.Add(Description("La barra de captura permite copiar, guardar y añadir marcas antes de terminar.",28,309,470,50));
            SectionTitle(sections[2],"Historial");historyEnabled=new CheckBox{Text="Conservar capturas recientes",Checked=draft.HistoryEnabled,AutoSize=true,Location=new Point(28,73)};sections[2].Controls.Add(historyEnabled);
            sections[2].Controls.Add(Label("Número máximo de capturas",28,125));historyLimit=new NumericUpDown{Minimum=1,Maximum=200,Value=Math.Max(1,Math.Min(200,draft.HistoryLimit)),Bounds=new Rectangle(28,157,120,30)};sections[2].Controls.Add(historyLimit);
            sections[2].Controls.Add(Description("30 de forma predeterminada · Puedes conservar hasta 200. Al superar el límite se eliminan las capturas más antiguas del historial.",28,201,470,62));
            sections[2].Controls.Add(Label("Ubicación del historial",28,290));TextBox path=new TextBox{Text=historyDirectory,ReadOnly=true,Bounds=new Rectangle(28,324,467,29)};sections[2].Controls.Add(path);
            Button open=new Button{Text="Abrir carpeta",Bounds=new Rectangle(28,366,150,32)};open.Click+=delegate{try{Directory.CreateDirectory(historyDirectory);Process.Start(new ProcessStartInfo(historyDirectory){UseShellExecute=true});}catch(Exception error){MessageBox.Show(this,"No se pudo abrir la carpeta.\n"+error.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};sections[2].Controls.Add(open);
            historyEnabled.CheckedChanged+=delegate{historyLimit.Enabled=historyEnabled.Checked;};historyLimit.Enabled=historyEnabled.Checked;
            SectionTitle(sections[3],"Atajos de captura");sections[3].Controls.Add(Description("Elige las teclas y combinaciones que quieras utilizar. Puedes añadir, cambiar y quitar atajos, o restablecer los valores iniciales.",28,75,470,72));
            Button shortcuts=new Button{Text="Configurar atajos…",Bounds=new Rectangle(28,168,217,36),FlatStyle=FlatStyle.Flat,ForeColor=Color.FromArgb(0,145,225),BackColor=Color.White};shortcuts.Click+=delegate{if(configureShortcuts!=null)configureShortcuts();};sections[3].Controls.Add(shortcuts);
            sections[3].Controls.Add(Description("Windows reserva algunas combinaciones. Los cambios guardados en la ventana de atajos se aplicarán al cerrarla.",28,229,470,70));
            navigation.SelectedIndexChanged+=delegate{ShowSection(navigation.SelectedIndex);};navigation.SelectedIndex=0;
        }
        private static Label Label(string text,int x,int y){return new Label{Text=text,Location=new Point(x,y),AutoSize=true};}
        private static Label Description(string text,int x,int y,int w,int h){return new Label{Text=text,Bounds=new Rectangle(x,y,w,h),ForeColor=Color.FromArgb(110,120,132),Font=new Font("Segoe UI",9)};}
        private static void SectionTitle(Panel panel,string text){panel.Controls.Add(new Label{Text=text,Font=new Font("Segoe UI",16),AutoSize=true,Location=new Point(28,23),ForeColor=Color.FromArgb(42,55,68)});}
        private void ShowSection(int index){for(int i=0;i<sections.Length;i++)sections[i].Visible=i==index;}
        private void DrawNavigation(object sender,DrawItemEventArgs e)
        {
            if(e.Index<0)return;bool selected=(e.State&DrawItemState.Selected)!=0;using(Brush background=new SolidBrush(selected?Color.FromArgb(234,245,253):navigation.BackColor))e.Graphics.FillRectangle(background,e.Bounds);
            if(selected)using(Brush b=new SolidBrush(Color.FromArgb(0,163,255)))e.Graphics.FillRectangle(b,e.Bounds.Left,e.Bounds.Top,3,e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics,navigation.Items[e.Index].ToString(),Font,new Rectangle(e.Bounds.Left+21,e.Bounds.Top,e.Bounds.Width-22,e.Bounds.Height),selected?Color.FromArgb(0,145,225):Color.FromArgb(65,77,90),TextFormatFlags.VerticalCenter|TextFormatFlags.Left);
        }
        private void SaveChanges()
        {
            try
            {
                string folder=saveDirectory.Text.Trim();if(String.IsNullOrWhiteSpace(folder))throw new ArgumentException("Elige una carpeta para guardar las capturas.");folder=Path.GetFullPath(folder);
                draft.StartWithWindows=startup.Checked;draft.StartMinimized=minimized.Checked;draft.CloseBehavior=(CloseBehavior)closeBehavior.SelectedIndex;draft.HistoryEnabled=historyEnabled.Checked;draft.HistoryLimit=(int)historyLimit.Value;draft.SaveDirectory=folder;draft.ImageFormat=format.SelectedItem.ToString();draft.HideMainDuringCapture=hideMain.Checked;
                ResultSettings=draft;DialogResult=DialogResult.OK;Close();
            }
            catch(Exception error){MessageBox.Show(this,error.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        }
    }
}
