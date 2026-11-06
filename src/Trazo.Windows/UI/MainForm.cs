using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Trazo
{
    internal sealed class MainForm : Form
    {
        private static readonly Color Blue=Color.FromArgb(0,163,255);
        private readonly EventWaitHandle captureRequest;
        private readonly System.Windows.Forms.Timer requestTimer;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip trayMenu;
        private readonly Label shortcutStatus,historyStatus;
        private readonly Native.LowLevelKeyboardProc hookCallback;
        private readonly Icon ownIcon;
        private readonly ToolTip dashboardTips=new ToolTip();
        private readonly FlowLayoutPanel gallery;
        private readonly EmptyHistoryControl emptyHistory;
        private readonly CaptureHistoryStore historyStore;
        private readonly bool previewMode;
        private readonly List<DashboardButton> selectionActions=new List<DashboardButton>();
        private AppSettings appSettings;
        private CaptureRecord selectedRecord;
        private IntPtr keyboardHook;
        private readonly HashSet<int> registeredHotkeys=new HashSet<int>();
        private readonly HashSet<int> ownedKeys=new HashSet<int>();
        private readonly HashSet<int> passedKeys=new HashSet<int>();
        private ShortcutSettings shortcutSettings;
        private bool configuringShortcuts,captureQueued,capturing,exiting,disposed,trayNoticeShown;

        [DllImport("user32.dll")]private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr first,IntPtr second);

        protected override bool ShowWithoutActivation {get{return previewMode;}}
        internal bool StartMinimizedPreference {get{return appSettings.StartMinimized;}}
        internal MainForm(EventWaitHandle captureRequest) : this(captureRequest,AppSettings.Load(),CaptureHistoryStore.DefaultDirectory,false) { }
        internal MainForm(EventWaitHandle captureRequest,AppSettings settings,string historyDirectory,bool previewMode)
        {
            this.captureRequest=captureRequest;this.previewMode=previewMode;appSettings=settings.Clone();this.historyDirectory=historyDirectory;
            shortcutSettings=previewMode?ShortcutSettings.CreateDefault():ShortcutSettings.Load();historyStore=new CaptureHistoryStore(historyDirectory);
            Text="Trazo";StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.None;
            ClientSize=new Size(980,620);MinimumSize=new Size(780,520);AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            BackColor=Color.FromArgb(245,246,250);Font=new Font("Segoe UI",10);ownIcon=CreateIcon();Icon=ownIcon;
            MaximizedBounds=Screen.FromControl(this).WorkingArea;

            Panel header=new Panel{Dock=DockStyle.Top,Height=42,BackColor=Blue};
            PictureBox mark=new PictureBox{Image=ownIcon.ToBitmap(),SizeMode=PictureBoxSizeMode.CenterImage,Dock=DockStyle.Left,Width=45};header.Controls.Add(mark);
            Label brand=new Label{Text="Trazo",Font=new Font("Segoe UI",12),ForeColor=Color.White,TextAlign=ContentAlignment.MiddleLeft,Dock=DockStyle.Left,Width=100};header.Controls.Add(brand);brand.BringToFront();
            Label caption=new Label{Text="Capturas de pantalla",ForeColor=Color.White,TextAlign=ContentAlignment.MiddleCenter,Dock=DockStyle.Fill};header.Controls.Add(caption);caption.BringToFront();
            FlowLayoutPanel frameButtons=new FlowLayoutPanel{Dock=DockStyle.Right,Width=126,FlowDirection=FlowDirection.LeftToRight,WrapContents=false,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=Blue};
            Button minimize=FrameButton("—");minimize.Click+=delegate{WindowState=FormWindowState.Minimized;};frameButtons.Controls.Add(minimize);
            Button maximize=FrameButton("□");maximize.Click+=delegate{ToggleMaximize();};frameButtons.Controls.Add(maximize);
            Button close=FrameButton("×");close.FlatAppearance.MouseOverBackColor=Color.FromArgb(232,68,78);close.Click+=delegate{Close();};frameButtons.Controls.Add(close);header.Controls.Add(frameButtons);frameButtons.BringToFront();
            TableLayoutPanel headerLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty,Padding=Padding.Empty};headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,45));headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,126));
            header.Controls.Clear();mark.Dock=DockStyle.Fill;brand.Dock=DockStyle.Fill;caption.Dock=DockStyle.Fill;frameButtons.Dock=DockStyle.Fill;mark.Margin=brand.Margin=caption.Margin=frameButtons.Margin=Padding.Empty;
            headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            headerLayout.Controls.Add(mark,0,0);headerLayout.Controls.Add(brand,1,0);headerLayout.Controls.Add(caption,2,0);headerLayout.Controls.Add(frameButtons,3,0);header.Controls.Add(headerLayout);mark.Disposed+=delegate{if(mark.Image!=null)mark.Image.Dispose();};
            MouseEventHandler drag=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0x00A1,new IntPtr(2),IntPtr.Zero);}};
            header.MouseDown+=drag;caption.MouseDown+=drag;brand.MouseDown+=drag;mark.MouseDown+=drag;
            header.DoubleClick+=delegate{ToggleMaximize();};caption.DoubleClick+=delegate{ToggleMaximize();};

            FlowLayoutPanel toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=60,BackColor=Color.White,Padding=new Padding(18,10,14,8),WrapContents=false};
            AddToolbarAction(toolbar,"capture","Capturar pantalla",delegate{BeginCapture(true);},172);
            AddToolbarAction(toolbar,"project","Abrir proyecto",OpenProject,155);
            AddToolbarAction(toolbar,"settings","Configuración",ConfigureSettings,155);
            Label section=new Label{Text="CAPTURAS RECIENTES",Dock=DockStyle.Top,Height=35,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(25,0,0,0),ForeColor=Color.FromArgb(103,116,130),Font=new Font("Segoe UI",9,FontStyle.Bold)};

            Panel content=new Panel{Dock=DockStyle.Fill,Padding=new Padding(24,0,24,0),BackColor=BackColor};
            Panel surface=new Panel{Dock=DockStyle.Fill,BackColor=Color.White};
            gallery=new FlowLayoutPanel{Dock=DockStyle.Fill,BackColor=Color.White,AutoScroll=true,WrapContents=true,Padding=new Padding(14),FlowDirection=FlowDirection.LeftToRight};
            emptyHistory=new EmptyHistoryControl{Dock=DockStyle.Fill,BackColor=Color.White};surface.Controls.Add(gallery);surface.Controls.Add(emptyHistory);content.Controls.Add(surface);

            Panel footer=new Panel{Dock=DockStyle.Bottom,Height=60,BackColor=BackColor,Padding=new Padding(22,9,22,8)};
            FlowLayoutPanel actions=new FlowLayoutPanel{Dock=DockStyle.Left,Width=370,Padding=Padding.Empty,WrapContents=false};
            AddSelectionAction(actions,"eye","Ver captura",PreviewSelected);
            AddSelectionAction(actions,"edit","Editar captura",EditSelected);
            AddSelectionAction(actions,"copy","Copiar captura",CopySelected);
            AddSelectionAction(actions,"save","Guardar como",SaveSelected);
            AddSelectionAction(actions,"delete","Eliminar del historial",DeleteSelected);
            DashboardButton folder=new DashboardButton("folder",""){Size=new Size(44,36)};folder.Click+=delegate{OpenHistoryFolder();};dashboardTips.SetToolTip(folder,"Abrir carpeta del historial");actions.Controls.Add(folder);
            footer.Controls.Add(actions);
            historyStatus=new Label{Dock=DockStyle.Right,Width=205,TextAlign=ContentAlignment.MiddleRight,ForeColor=Color.FromArgb(110,120,132),Font=new Font("Segoe UI",9)};footer.Controls.Add(historyStatus);
            shortcutStatus=new Label{Dock=DockStyle.Bottom,Height=24,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(25,0,0,0),ForeColor=Color.FromArgb(115,125,137),Font=new Font("Segoe UI",8.5f)};
            Controls.Add(content);Controls.Add(footer);Controls.Add(shortcutStatus);Controls.Add(section);Controls.Add(toolbar);Controls.Add(header);

            trayMenu=new ContextMenuStrip();trayMenu.Items.Add("Capturar pantalla",null,delegate{BeginCapture(false);});trayMenu.Items.Add("Mostrar Trazo",null,delegate{ShowMain();});trayMenu.Items.Add("Configuración…",null,delegate{ConfigureSettings();});trayMenu.Items.Add("Abrir proyecto…",null,delegate{OpenProject();});trayMenu.Items.Add(new ToolStripSeparator());trayMenu.Items.Add("Cerrar Trazo",null,delegate{ExitTrazo();});
            tray=new NotifyIcon{Icon=ownIcon,Text="Trazo · Capturar pantalla",Visible=!previewMode,ContextMenuStrip=trayMenu};tray.DoubleClick+=delegate{ShowMain();};
            hookCallback=KeyboardHook;requestTimer=new System.Windows.Forms.Timer{Interval=180};requestTimer.Tick+=delegate{if(captureRequest!=null&&captureRequest.WaitOne(0))BeginCapture(false);};
            Shown+=delegate{if(!previewMode){InstallShortcut();requestTimer.Start();}};
            FormClosing+=OnClosing;Resize+=delegate{if(!previewMode&&WindowState==FormWindowState.Minimized)HideToTray(false);};
            RefreshHistory();
        }
        private Button FrameButton(string text)
        {
            Button b=new Button{Text=text,Size=new Size(42,42),FlatStyle=FlatStyle.Flat,BackColor=Blue,ForeColor=Color.White,Font=new Font("Segoe UI",15),Margin=Padding.Empty,TabStop=false};b.FlatAppearance.BorderSize=0;b.FlatAppearance.MouseOverBackColor=Color.FromArgb(0,145,235);return b;
        }
        private void ToggleMaximize(){MaximizedBounds=Screen.FromControl(this).WorkingArea;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
        private void AddToolbarAction(FlowLayoutPanel panel,string glyph,string text,Action action,int width)
        {DashboardButton button=new DashboardButton(glyph,text){Size=new Size(width,38),Margin=new Padding(0,0,8,0)};button.Click+=delegate{action();};panel.Controls.Add(button);}
        private void AddSelectionAction(FlowLayoutPanel panel,string glyph,string hint,Action action)
        {DashboardButton button=new DashboardButton(glyph,""){Size=new Size(44,36),Margin=new Padding(0,0,5,0)};button.Click+=delegate{action();};dashboardTips.SetToolTip(button,hint);panel.Controls.Add(button);selectionActions.Add(button);}
        internal void ShowFromExternalRequest(){ShowMain();}
        internal void RequestExit(){ExitTrazo();}
        internal void OpenProjectFile(string path)
        {
            if(capturing||configuringShortcuts||exiting||IsDisposed)return;
            capturing=true;UnregisterFallbacks();
            try
            {
                using(ProjectData data=ProjectStore.Load(path))
                {Hide();using(OverlayForm overlay=new OverlayForm(data)){PrepareOverlay(overlay);overlay.ShowDialog();}}
            }
            catch(Exception error){MessageBox.Show(this,"No se pudo abrir el proyecto.\n"+error.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            finally{capturing=false;if(!previewMode&&!exiting&&!IsDisposed){InstallShortcut();ShowMain();}}
        }
        private void HideToTray(bool notify)
        {
            Hide();
            if(!previewMode&&(!trayNoticeShown||notify)){trayNoticeShown=true;tray.ShowBalloonTip(4500,"Trazo sigue disponible","La aplicación permanece junto al reloj. Puedes capturar con tus atajos o salir desde su icono.",ToolTipIcon.Info);}
        }
        internal void RefreshHistory()
        {
            string previous=selectedRecord==null?null:selectedRecord.Id;selectedRecord=null;
            while(gallery.Controls.Count>0){Control item=gallery.Controls[0];gallery.Controls.RemoveAt(0);item.Dispose();}
            List<CaptureRecord> entries;
            try{entries=historyStore.Load();if(!previewMode&&appSettings.HistoryEnabled&&entries.Count>appSettings.HistoryLimit){historyStore.Prune(appSettings.HistoryLimit);entries=historyStore.Load();}}
            catch(Exception error){entries=new List<CaptureRecord>();shortcutStatus.Text="No se pudo abrir el historial: "+error.Message;}
            foreach(CaptureRecord record in entries)
            {
                CaptureTile tile=new CaptureTile(record);tile.Click+=delegate{SelectRecord(record);};tile.DoubleClick+=delegate{SelectRecord(record);PreviewSelected();};gallery.Controls.Add(tile);
                if(record.Id==previous)selectedRecord=record;
            }
            if(selectedRecord==null&&entries.Count>0)selectedRecord=entries[0];
            emptyHistory.Visible=entries.Count==0;gallery.Visible=entries.Count>0;
            historyStatus.Text=entries.Count+" / "+appSettings.HistoryLimit+" capturas";
            UpdateRecordSelection();
        }
        private void SelectRecord(CaptureRecord record){selectedRecord=record;UpdateRecordSelection();}
        private void UpdateRecordSelection()
        {foreach(Control control in gallery.Controls){CaptureTile tile=control as CaptureTile;if(tile!=null){tile.Selected=selectedRecord!=null&&tile.Record.Id==selectedRecord.Id;tile.Invalidate();}}foreach(DashboardButton button in selectionActions)button.Enabled=selectedRecord!=null;}
        private void PrepareOverlay(OverlayForm overlay)
        {
            overlay.DefaultSaveDirectory=appSettings.SaveDirectory;overlay.DefaultImageFormat=appSettings.ImageFormat;
            overlay.ImageExported+=delegate(Bitmap image)
            {
                if(!appSettings.HistoryEnabled)return;
                try{historyStore.Add(image,appSettings.HistoryLimit);RefreshHistory();}
                catch(Exception error){if(!previewMode)tray.ShowBalloonTip(5000,"Trazo · Historial","La captura terminó, pero no se pudo guardar en el historial: "+error.Message,ToolTipIcon.Warning);}
            };
        }
        private void PreviewSelected()
        {
            CaptureRecord record=selectedRecord;if(record==null)return;
            try{using(CapturePreviewForm preview=new CapturePreviewForm(record)){if(preview.ShowDialog(this)==DialogResult.Yes)EditRecord(record);}}
            catch(Exception error){MessageBox.Show(this,"No se pudo abrir la captura.\n"+error.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);RefreshHistory();}
        }
        private void EditSelected(){if(selectedRecord!=null)EditRecord(selectedRecord);}
        private void EditRecord(CaptureRecord record)
        {
            if(capturing||configuringShortcuts||exiting)return;capturing=true;UnregisterFallbacks();
            try
            {
                using(ProjectData data=new ProjectData())
                {
                    using(Image file=Image.FromFile(record.FullPath))data.Screenshot=new Bitmap(file);
                    data.Selection=new Rectangle(Point.Empty,data.Screenshot.Size);data.VirtualBounds=new Rectangle(Point.Empty,data.Screenshot.Size);data.Annotations=new Annotation[0];data.Effects=new ExportOptions();
                    Hide();using(OverlayForm editor=new OverlayForm(data)){PrepareOverlay(editor);editor.ShowDialog();}
                }
            }
            catch(Exception error){MessageBox.Show(this,"No se pudo editar la captura.\n"+error.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            finally{capturing=false;if(!previewMode&&!exiting){InstallShortcut();ShowMain();}}
        }
        private void CopySelected()
        {
            if(selectedRecord==null)return;
            try{using(Image image=Image.FromFile(selectedRecord.FullPath))using(Bitmap bitmap=new Bitmap(image))Clipboard.SetDataObject(bitmap,true,8,80);shortcutStatus.Text="Captura copiada al portapapeles.";}
            catch(Exception error){MessageBox.Show(this,"No se pudo copiar la captura.\n"+error.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        }
        private void SaveSelected()
        {
            if(selectedRecord==null)return;
            using(SaveFileDialog dialog=new SaveFileDialog{Title="Guardar captura",Filter="Imagen PNG|*.png|Imagen JPEG|*.jpg|Mapa de bits|*.bmp",FileName="Trazo_"+selectedRecord.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss")+".png",AddExtension=true,DefaultExt="png",OverwritePrompt=true})
            {
                if(Directory.Exists(appSettings.SaveDirectory))dialog.InitialDirectory=appSettings.SaveDirectory;
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                try{using(Image source=Image.FromFile(selectedRecord.FullPath))using(Bitmap bitmap=new Bitmap(source))CapturePreviewForm.WriteImage(bitmap,dialog.FileName);}
                catch(Exception error){MessageBox.Show(this,"No se pudo guardar la captura.\n"+error.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            }
        }
        private void DeleteSelected()
        {
            if(selectedRecord==null)return;
            if(MessageBox.Show(this,"¿Eliminar esta captura del historial? Los archivos que hayas exportado se conservarán.","Trazo",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            try{if(!historyStore.Delete(selectedRecord))throw new IOException("La captura no se pudo eliminar. Si está abierta en otro programa, ciérrala e inténtalo de nuevo.");RefreshHistory();}
            catch(Exception error){MessageBox.Show(this,"No se pudo eliminar la captura.\n"+error.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        }
        private void OpenHistoryFolder()
        {try{Directory.CreateDirectory(historyStoreDirectory);Process.Start(new ProcessStartInfo(historyStoreDirectory){UseShellExecute=true});}catch(Exception error){MessageBox.Show(this,"No se pudo abrir la carpeta.\n"+error.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
        private string historyStoreDirectory {get{return historyDirectory;}}
        private readonly string historyDirectory;
        private void ConfigureSettings()
        {
            if(capturing||configuringShortcuts||exiting||previewMode)return;configuringShortcuts=true;UnregisterFallbacks();
            try
            {
                bool oldStartup=StartupRegistration.IsEnabled();string oldStartupCommand=StartupRegistration.ReadCommand();
                using(SettingsForm dialog=new SettingsForm(appSettings,historyDirectory,oldStartup,delegate{EditShortcutsFromSettings();}))
                {
                    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                    AppSettings next=dialog.ResultSettings;
                    bool startupChanged=next.StartWithWindows!=oldStartup||(next.StartWithWindows&&next.StartMinimized!=(oldStartupCommand!=null&&oldStartupCommand.EndsWith(" --background",StringComparison.OrdinalIgnoreCase)));
                    try{if(startupChanged)StartupRegistration.SetEnabled(next.StartWithWindows,next.StartMinimized);next.Save();}
                    catch(Exception error){if(startupChanged)try{StartupRegistration.RestoreCommand(oldStartupCommand);}catch{}MessageBox.Show(this,"No se pudo guardar la configuración.\n"+error.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
                    appSettings=next;
                    try{if(appSettings.HistoryEnabled)historyStore.Prune(appSettings.HistoryLimit);RefreshHistory();}
                    catch(Exception error){MessageBox.Show(this,"La configuración se guardó, pero no se pudo ajustar el historial.\n"+error.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
                }
            }
            finally{configuringShortcuts=false;if(!exiting&&!IsDisposed)InstallShortcut();}
        }
        private void EditShortcutsFromSettings()
        {using(ShortcutSettingsForm dialog=new ShortcutSettingsForm(shortcutSettings))if(dialog.ShowDialog(this)==DialogResult.OK)shortcutSettings=dialog.ResultSettings;}
        private void InstallShortcut()
        {
            UnregisterFallbacks();
            if(shortcutSettings.Shortcuts.Count==0)
            {
                if(keyboardHook!=IntPtr.Zero){Native.UnhookWindowsHookEx(keyboardHook);keyboardHook=IntPtr.Zero;}
                shortcutStatus.Text="Sin atajos activos. Usa el botón Capturar.";shortcutStatus.ForeColor=Color.FromArgb(110,119,130);return;
            }
            if(keyboardHook==IntPtr.Zero)keyboardHook=Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL,hookCallback,Native.GetModuleHandle(null),0);
            List<string> failures=new List<string>();
            for(int i=0;i<shortcutSettings.Shortcuts.Count;i++)
            {
                Keys shortcut=shortcutSettings.Shortcuts[i];
                // Los atajos con Win se reservan con Windows para conservar su comportamiento de modificador.
                if(keyboardHook!=IntPtr.Zero&&(shortcut&ShortcutSettings.WindowsModifier)==0)continue;
                uint modifiers=Native.MOD_NOREPEAT;
                if((shortcut&Keys.Alt)!=0)modifiers|=1;if((shortcut&Keys.Control)!=0)modifiers|=2;if((shortcut&Keys.Shift)!=0)modifiers|=4;if((shortcut&ShortcutSettings.WindowsModifier)!=0)modifiers|=8;
                int id=Native.HOTKEY_ID+i;
                if(Native.RegisterHotKey(Handle,id,modifiers,(uint)(shortcut&Keys.KeyCode)))registeredHotkeys.Add(id);
                else failures.Add(ShortcutSettings.Format(shortcut)+": "+new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
            if(failures.Count>0)
            {
                shortcutStatus.Text=failures.Count+" atajo(s) no disponible(s). Puedes capturar desde el botón.";shortcutStatus.ForeColor=Color.FromArgb(180,70,35);
                tray.ShowBalloonTip(7000,"Trazo · Atajos",String.Join("\n",failures.ToArray()),ToolTipIcon.Warning);
            }
            else
            {
                List<string> names=new List<string>();foreach(Keys shortcut in shortcutSettings.Shortcuts)names.Add(ShortcutSettings.Format(shortcut));
                shortcutStatus.Text=names.Count<=3?"Atajos: "+String.Join(" · ",names.ToArray()):names.Count+" atajos de captura activos";
                shortcutStatus.ForeColor=Color.FromArgb(110,119,130);
            }
        }
        private void UnregisterFallbacks()
        {
            if(IsHandleCreated)foreach(int id in registeredHotkeys)Native.UnregisterHotKey(Handle,id);
            registeredHotkeys.Clear();
        }
        private void ConfigureShortcuts()
        {
            if(capturing||configuringShortcuts||exiting||IsDisposed)return;
            configuringShortcuts=true;UnregisterFallbacks();
            try
            {
                using(ShortcutSettingsForm dialog=new ShortcutSettingsForm(shortcutSettings))
                    if(dialog.ShowDialog(this)==DialogResult.OK)shortcutSettings=dialog.ResultSettings;
            }
            finally { configuringShortcuts=false;if(!exiting&&!IsDisposed)InstallShortcut(); }
        }

        private IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && !exiting && !IsDisposed)
            {
                Native.KeyboardData key = (Native.KeyboardData)Marshal.PtrToStructure(lParam, typeof(Native.KeyboardData));
                int keyCode=(int)key.vkCode,message=wParam.ToInt32();
                bool down=message==Native.WM_KEYDOWN||message==Native.WM_SYSKEYDOWN;
                bool up=message==Native.WM_KEYUP||message==Native.WM_SYSKEYUP;
                if(up&&ownedKeys.Remove(keyCode))return new IntPtr(1);
                if(up&&passedKeys.Remove(keyCode))return Native.CallNextHookEx(keyboardHook,code,wParam,lParam);
                if(down&&ownedKeys.Contains(keyCode))return new IntPtr(1);
                if(down&&passedKeys.Contains(keyCode))return Native.CallNextHookEx(keyboardHook,code,wParam,lParam);
                // Las teclas del editor y del cuadro de configuración siguen disponibles aunque sean atajos globales.
                Keys modifiers=Native.ReadShortcutModifiers();
                bool enabled=!capturing&&!configuringShortcuts&&(modifiers&ShortcutSettings.WindowsModifier)==0;
                if(down)
                {
                    if(enabled&&shortcutSettings.Matches(keyCode,modifiers)){ownedKeys.Add(keyCode);QueueCapture();return new IntPtr(1);}
                    passedKeys.Add(keyCode);
                }
                // Algunos teclados emiten únicamente la liberación de la tecla de captura.
                if(up&&keyCode==Native.VK_SNAPSHOT&&enabled&&shortcutSettings.Matches(keyCode,modifiers)){QueueCapture();return new IntPtr(1);}
            }
            return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
        }

        private void QueueCapture()
        {
            if (captureQueued || capturing || configuringShortcuts || exiting || !IsHandleCreated) return;
            captureQueued = true;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    captureQueued = false;
                    if (!exiting && !IsDisposed) BeginCapture(false);
                }));
            }
            catch (InvalidOperationException) { captureQueued = false; }
        }

        protected override void WndProc(ref Message message)
        {
            if(message.Msg==0x0084&&WindowState==FormWindowState.Normal)
            {
                Point p=PointToClient(new Point((short)(message.LParam.ToInt64()&0xffff),(short)((message.LParam.ToInt64()>>16)&0xffff)));
                bool left=p.X<7,right=p.X>=ClientSize.Width-7,top=p.Y<7,bottom=p.Y>=ClientSize.Height-7;
                int hit=left&&top?13:right&&top?14:left&&bottom?16:right&&bottom?17:left?10:right?11:top?12:bottom?15:0;
                if(hit!=0){message.Result=new IntPtr(hit);return;}
            }
            if (message.Msg == Native.WM_HOTKEY && registeredHotkeys.Contains(message.WParam.ToInt32()))
            {
                QueueCapture();
                return;
            }
            base.WndProc(ref message);
        }

        internal void BeginCapture(bool restoreMain)
        {
            if (capturing || configuringShortcuts || exiting || IsDisposed) return;
            capturing = true;
            UnregisterFallbacks();
            bool showAfter = restoreMain && Visible && WindowState != FormWindowState.Minimized;
            if(appSettings.HideMainDuringCapture)Hide();
            System.Windows.Forms.Timer delay = new System.Windows.Forms.Timer { Interval = 180 };
            delay.Tick += delegate
            {
                delay.Stop();
                delay.Dispose();
                try
                {
                    if (exiting || IsDisposed) return;
                    Rectangle bounds = SystemInformation.VirtualScreen;
                    if (bounds.Width < 1 || bounds.Height < 1) throw new InvalidOperationException("No se encontró una pantalla disponible.");
                    CaptureWindow[] windows = WindowDetection.Snapshot(Handle);
                    using (Bitmap screenshot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics graphics = Graphics.FromImage(screenshot))
                            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                        using (OverlayForm overlay = new OverlayForm(screenshot, bounds, windows))
                        {
                            PrepareOverlay(overlay);overlay.ShowDialog();
                        }
                    }
                }
                catch (Exception error)
                {
                    tray.ShowBalloonTip(7000, "Trazo · No se pudo capturar", error.Message, ToolTipIcon.Error);
                    showAfter = true;
                }
                finally
                {
                    capturing = false;
                    if(!exiting&&!IsDisposed)InstallShortcut();
                    if (showAfter && !exiting && !IsDisposed) ShowMain();
                }
            };
            delay.Start();
        }

        internal void OpenProject()
        {
            if (capturing || configuringShortcuts || exiting || IsDisposed) return;
            capturing = true;
            UnregisterFallbacks();
            try
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Abrir proyecto · Trazo";
                    dialog.Filter = "Proyecto editable de Trazo|*.trazo";
                    dialog.CheckFileExists = true;
                    dialog.Multiselect = false;
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    using (ProjectData data = ProjectStore.Load(dialog.FileName))
                    {
                        Hide();
                        using (OverlayForm overlay = new OverlayForm(data))
                        {
                            PrepareOverlay(overlay);overlay.ShowDialog();
                        }
                    }
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "No se pudo abrir el proyecto.\n" + error.Message, "Trazo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                capturing = false;
                if(!exiting&&!IsDisposed)InstallShortcut();
                if (!exiting && !IsDisposed) ShowMain();
            }
        }

        private void ShowMain()
        {
            if (capturing || exiting || IsDisposed) return;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if(previewMode||exiting||e.CloseReason!=CloseReason.UserClosing)return;
            e.Cancel=true;CloseBehavior behavior=appSettings.CloseBehavior;
            if(behavior==CloseBehavior.Ask)
            {
                configuringShortcuts=true;UnregisterFallbacks();
                try
                {
                    using(ClosePromptForm prompt=new ClosePromptForm())
                    {
                        if(prompt.ShowDialog(this)!=DialogResult.OK)return;
                        behavior=prompt.ResultBehavior;
                        if(prompt.RememberChoice)
                        {
                            try{appSettings.CloseBehavior=behavior;appSettings.Save();}
                            catch(Exception error){MessageBox.Show(this,"No se pudo recordar la elección.\n"+error.Message,"Trazo",MessageBoxButtons.OK,MessageBoxIcon.Warning);appSettings.CloseBehavior=CloseBehavior.Ask;}
                        }
                    }
                }
                finally{configuringShortcuts=false;if(!exiting&&!IsDisposed)InstallShortcut();}
            }
            if(behavior==CloseBehavior.Tray){HideToTray(true);return;}
            exiting=true;e.Cancel=false;
        }

        private void ExitTrazo()
        {
            exiting = true;
            Close();
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                exiting = true;
                if (keyboardHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
                UnregisterFallbacks();
                requestTimer.Stop();
                requestTimer.Dispose();
                tray.Visible = false;
                tray.Dispose();
                trayMenu.Dispose();
                ownIcon.Dispose();
                dashboardTips.Dispose();
            }
            base.Dispose(disposing);
        }

        private static Icon CreateIcon()
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (SolidBrush blue = new SolidBrush(Blue))
            using (Pen white = new Pen(Color.White, 2.5F))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                graphics.FillEllipse(blue, 0, 0, 31, 31);
                graphics.DrawLines(white, new PointF[] { new PointF(9, 15), new PointF(9, 9), new PointF(15, 9) });
                graphics.DrawLines(white, new PointF[] { new PointF(17, 9), new PointF(23, 9), new PointF(23, 15) });
                graphics.DrawLines(white, new PointF[] { new PointF(23, 17), new PointF(23, 23), new PointF(17, 23) });
                graphics.DrawLines(white, new PointF[] { new PointF(15, 23), new PointF(9, 23), new PointF(9, 17) });
                IntPtr handle = bitmap.GetHicon();
                try { using (Icon borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
                finally { Native.DestroyIcon(handle); }
            }
        }
    }
}

namespace Trazo
{
    internal sealed class DashboardButton:Control
    {
        private readonly string glyph;
        private bool hover;
        internal DashboardButton(string glyph,string text)
        {
            this.glyph=glyph;Text=text;Cursor=Cursors.Hand;TabStop=true;AccessibleName=String.IsNullOrEmpty(text)?glyph:text;AccessibleRole=AccessibleRole.PushButton;
            Font=new Font("Segoe UI",10);SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);
        }
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
        protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space){OnClick(EventArgs.Empty);e.SuppressKeyPress=true;}}
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Color.White:Parent.BackColor);
            if(hover&&Enabled)using(Brush shade=new SolidBrush(Color.FromArgb(231,245,253)))g.FillRectangle(shade,0,0,Width,Height);
            Color color=Enabled?(hover?Color.FromArgb(0,150,235):Color.FromArgb(92,106,120)):Color.FromArgb(178,186,194);
            float x=String.IsNullOrEmpty(Text)?(Width-23)/2f:11,y=(Height-23)/2f;
            GraphicsState state=g.Save();g.TranslateTransform(x,y);
            using(Pen p=new Pen(color,1.5f))using(Brush b=new SolidBrush(color))
            {
                p.StartCap=LineCap.Round;p.EndCap=LineCap.Round;p.LineJoin=LineJoin.Round;
                switch(glyph)
                {
                    case "capture":g.DrawRectangle(p,3,4,17,14);g.DrawLine(p,8,1,8,7);g.DrawLine(p,0,9,6,9);g.DrawLine(p,17,15,23,15);g.DrawLine(p,15,18,15,22);break;
                    case "project":g.DrawRectangle(p,4,3,15,17);g.DrawLine(p,7,8,16,8);g.DrawLine(p,7,12,16,12);g.DrawLine(p,7,16,12,16);break;
                    case "settings":g.DrawEllipse(p,5,5,13,13);g.DrawEllipse(p,9,9,5,5);for(int i=0;i<8;i++){double a=i*Math.PI/4;g.DrawLine(p,11.5f+(float)Math.Cos(a)*7,11.5f+(float)Math.Sin(a)*7,11.5f+(float)Math.Cos(a)*10,11.5f+(float)Math.Sin(a)*10);}break;
                    case "eye":g.DrawBezier(p,1,11,7,2,16,2,22,11);g.DrawBezier(p,1,11,7,20,16,20,22,11);g.DrawEllipse(p,8,7,7,7);break;
                    case "edit":g.DrawPolygon(p,new Point[]{new Point(4,15),new Point(15,4),new Point(20,9),new Point(9,20),new Point(3,21)});g.DrawLine(p,12,7,17,12);break;
                    case "copy":g.DrawRectangle(p,8,7,12,15);g.DrawLines(p,new Point[]{new Point(16,3),new Point(4,3),new Point(4,17)});break;
                    case "save":g.DrawRectangle(p,4,3,16,18);g.DrawRectangle(p,8,3,8,6);g.DrawRectangle(p,8,13,8,8);break;
                    case "delete":g.DrawLine(p,4,6,20,6);g.DrawRectangle(p,8,2,8,4);g.DrawLines(p,new Point[]{new Point(6,6),new Point(7,21),new Point(17,21),new Point(18,6)});g.DrawLine(p,10,10,10,17);g.DrawLine(p,14,10,14,17);break;
                    case "folder":g.DrawLines(p,new Point[]{new Point(2,19),new Point(2,5),new Point(9,5),new Point(11,8),new Point(21,8),new Point(21,19),new Point(2,19)});break;
                }
            }
            g.Restore(state);
            if(!String.IsNullOrEmpty(Text))TextRenderer.DrawText(g,Text,Font,new Rectangle(43,0,Width-48,Height),color,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
            if(Focused)ControlPaint.DrawFocusRectangle(g,new Rectangle(2,2,Width-5,Height-5),color,BackColor);
        }
    }

    internal sealed class EmptyHistoryControl:Control
    {
        internal EmptyHistoryControl(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            int cx=ClientSize.Width/2,cy=Math.Max(75,ClientSize.Height/2-42);
            using(Pen p=new Pen(Color.FromArgb(185,198,210),2.3f))
            {
                g.DrawLines(p,new Point[]{new Point(cx-33,cy-8),new Point(cx-33,cy-27),new Point(cx-14,cy-27)});
                g.DrawLines(p,new Point[]{new Point(cx+14,cy-27),new Point(cx+33,cy-27),new Point(cx+33,cy-8)});
                g.DrawLines(p,new Point[]{new Point(cx+33,cy+8),new Point(cx+33,cy+27),new Point(cx+14,cy+27)});
                g.DrawLines(p,new Point[]{new Point(cx-14,cy+27),new Point(cx-33,cy+27),new Point(cx-33,cy+8)});
                g.DrawLine(p,cx-8,cy,cx+8,cy);g.DrawLine(p,cx,cy-8,cx,cy+8);
            }
            using(Font heading=new Font("Segoe UI",14))TextRenderer.DrawText(g,"Tus capturas aparecerán aquí",heading,new Rectangle(10,cy+52,Width-20,32),Color.FromArgb(72,86,101),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            using(Font description=new Font("Segoe UI",10))TextRenderer.DrawText(g,"Haz clic en Capturar pantalla o usa uno de tus atajos.",description,new Rectangle(10,cy+91,Width-20,45),Color.FromArgb(135,145,156),TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak);
        }
    }

    internal sealed class CaptureTile:Control
    {
        internal readonly CaptureRecord Record;
        private readonly Bitmap thumbnail;
        internal bool Selected;
        internal CaptureTile(CaptureRecord record)
        {
            Record=record;Size=new Size(218,182);Margin=new Padding(8);Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.ListItem;AccessibleName="Captura "+record.CreatedUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm");
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.StandardDoubleClick,true);
            try
            {
                thumbnail=new Bitmap(202,122);
                using(Graphics g=Graphics.FromImage(thumbnail))using(Image source=Image.FromFile(record.FullPath))
                {
                    g.Clear(Color.FromArgb(242,245,249));g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    double scale=Math.Min(202.0/source.Width,122.0/source.Height);int w=Math.Max(1,(int)(source.Width*scale)),h=Math.Max(1,(int)(source.Height*scale));g.DrawImage(source,(202-w)/2,(122-h)/2,w,h);
                }
            }
            catch{if(thumbnail!=null){thumbnail.Dispose();thumbnail=null;}}
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.Clear(Color.White);using(Brush back=new SolidBrush(Color.FromArgb(243,246,249)))g.FillRectangle(back,8,8,202,122);
            if(thumbnail!=null)g.DrawImageUnscaled(thumbnail,8,8);
            using(Pen p=new Pen(Selected?Color.FromArgb(0,163,255):Color.FromArgb(225,231,237),Selected?2:1))g.DrawRectangle(p,1,1,Width-3,Height-3);
            using(Font date=new Font("Segoe UI",9))TextRenderer.DrawText(g,Record.CreatedUtc.ToLocalTime().ToString("dd MMM yyyy · HH:mm"),date,new Rectangle(9,139,200,20),Color.FromArgb(61,77,94),TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
            using(Font size=new Font("Segoe UI",8))TextRenderer.DrawText(g,Record.Width+" × "+Record.Height,size,new Rectangle(9,159,200,18),Color.FromArgb(143,154,166));
        }
        protected override void Dispose(bool disposing){if(disposing&&thumbnail!=null)thumbnail.Dispose();base.Dispose(disposing);}
    }
}
