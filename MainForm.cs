using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Trazo
{
    internal sealed class MainForm : Form
    {
        private static readonly Color Blue = Color.FromArgb(0, 163, 255);
        private readonly EventWaitHandle captureRequest;
        private readonly System.Windows.Forms.Timer requestTimer;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip trayMenu;
        private readonly Label shortcutStatus;
        private readonly Native.LowLevelKeyboardProc hookCallback;
        private readonly Icon ownIcon;
        private IntPtr keyboardHook;
        private readonly HashSet<int> registeredHotkeys=new HashSet<int>();
        private readonly HashSet<int> ownedKeys=new HashSet<int>();
        private readonly HashSet<int> passedKeys=new HashSet<int>();
        private ShortcutSettings shortcutSettings;
        private bool configuringShortcuts;
        private bool captureQueued;
        private bool capturing;
        private bool exiting;
        private bool disposed;

        internal MainForm(EventWaitHandle captureRequest)
        {
            this.captureRequest = captureRequest;
            shortcutSettings=ShortcutSettings.Load();
            Text = "Trazo";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(580, 365);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(246, 247, 251);
            Font = new Font("Segoe UI", 10F);
            ownIcon = CreateIcon();
            Icon = ownIcon;

            Panel header = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Blue };
            Label brand = new Label { Text = "Trazo", ForeColor = Color.White, Font = new Font("Segoe UI", 15F), AutoSize = true, Location = new Point(23, 8) };
            header.Controls.Add(brand);
            Label headerCaption = new Label { Text = "Captura de pantalla", ForeColor = Color.White, AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(406, 16) };
            header.Controls.Add(headerCaption);
            Controls.Add(header);

            Panel body = new Panel { BackColor = Color.White, Location = new Point(18, 67), Size = new Size(544, 242), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(body);
            Label heading = new Label { Text = "Captura justo lo que necesitas", Font = new Font("Segoe UI", 17F), ForeColor = Color.FromArgb(43, 50, 60), AutoSize = true, Location = new Point(88, 23) };
            body.Controls.Add(heading);
            Label explanation = new Label { Text = "Selecciona un área, añade marcas y copia o guarda.", ForeColor = Color.FromArgb(100, 108, 120), AutoSize = true, Location = new Point(85, 65) };
            body.Controls.Add(explanation);
            Button captureButton = new Button { Text = "  Capturar pantalla", BackColor = Blue, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 12F), Size = new Size(232, 47), Location = new Point(156, 105), Cursor = Cursors.Hand, AccessibleName = "Capturar pantalla" };
            captureButton.FlatAppearance.BorderSize = 0;
            captureButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 143, 235);
            captureButton.Click += delegate { BeginCapture(true); };
            body.Controls.Add(captureButton);
            LinkLabel shortcutLink=new LinkLabel { Text="Configurar atajos…", AutoSize=true, Location=new Point(140,176), LinkColor=Color.FromArgb(42,143,203), ActiveLinkColor=Blue, VisitedLinkColor=Color.FromArgb(42,143,203), Cursor=Cursors.Hand, AccessibleName="Configurar atajos de captura" };
            shortcutLink.LinkClicked+=delegate { ConfigureShortcuts(); };body.Controls.Add(shortcutLink);
            LinkLabel openProject = new LinkLabel { Text = "Abrir proyecto…", AutoSize = true, Location = new Point(296, 176), LinkColor = Color.FromArgb(42, 143, 203), ActiveLinkColor = Blue, VisitedLinkColor = Color.FromArgb(42, 143, 203), Cursor = Cursors.Hand, AccessibleName = "Abrir proyecto editable de Trazo" };
            openProject.LinkClicked += delegate { OpenProject(); };
            body.Controls.Add(openProject);
            shortcutStatus = new Label { Text = "Funciona desde cualquier aplicación", ForeColor = Color.FromArgb(110, 119, 130), Font = new Font("Segoe UI", 9F), TextAlign = ContentAlignment.MiddleCenter, Location = new Point(20, 207), Size = new Size(504, 24) };
            body.Controls.Add(shortcutStatus);
            Label footer = new Label { Text = "Trazo permanece disponible junto al reloj al cerrar esta ventana.", AutoSize = true, ForeColor = Color.FromArgb(115, 123, 135), Font = new Font("Segoe UI", 9F), Location = new Point(47, 329) };
            Controls.Add(footer);

            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Capturar pantalla", null, delegate { BeginCapture(false); });
            trayMenu.Items.Add("Abrir proyecto…", null, delegate { OpenProject(); });
            trayMenu.Items.Add("Mostrar Trazo", null, delegate { ShowMain(); });
            trayMenu.Items.Add("Configurar atajos…",null,delegate { ConfigureShortcuts(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Cerrar Trazo", null, delegate { ExitTrazo(); });
            tray = new NotifyIcon { Icon = ownIcon, Text = "Trazo · Capturar pantalla", Visible = true, ContextMenuStrip = trayMenu };
            tray.DoubleClick += delegate { BeginCapture(false); };

            hookCallback = KeyboardHook;
            requestTimer = new System.Windows.Forms.Timer { Interval = 180 };
            requestTimer.Tick += delegate { if (captureRequest.WaitOne(0)) BeginCapture(false); };
            Shown += delegate { InstallShortcut(); requestTimer.Start(); };
            FormClosing += OnClosing;
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
        }

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
            Hide();
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
                            overlay.ShowDialog();
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
                            overlay.ShowDialog();
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
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
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
