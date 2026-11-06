using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Trazo
{
    internal sealed class CaptureWindow
    {
        internal Rectangle Bounds;
        internal CaptureWindow[] Children;
    }

    // La captura conserva esta geometría antes de mostrar la superposición.
    // No se leen títulos, contenido de controles ni texto de otras aplicaciones.
    internal static class WindowDetection
    {
        private const int MaxWindows = 512;
        private const int MaxChildren = 1600;
        private const int MaxDepth = 12;
        private const uint GW_CHILD = 5;
        private const uint GW_HWNDNEXT = 2;
        private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { internal int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder className, int capacity);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int GetDwmRectangle(IntPtr window, int attribute, out NativeRect rect, int bytes);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int GetDwmInteger(IntPtr window, int attribute, out int value, int bytes);

        internal static CaptureWindow[] Snapshot(IntPtr excludedWindow)
        {
            List<CaptureWindow> result = new List<CaptureWindow>();
            uint ownProcess = (uint)Process.GetCurrentProcess().Id;
            int childrenRemaining = MaxChildren;
            Rectangle desktop = SystemInformation.VirtualScreen;
            EnumWindowProc callback = delegate(IntPtr window, IntPtr parameter)
            {
                if (result.Count >= MaxWindows) return false;
                if (window == excludedWindow || !IsWindowVisible(window) || IsIconic(window)) return true;
                uint process;
                GetWindowThreadProcessId(window, out process);
                if (process == ownProcess || IsCloaked(window) || IsDesktopShell(window)) return true;
                Rectangle bounds;
                if (!TryBounds(window, true, out bounds)) return true;
                if (Rectangle.Intersect(bounds, desktop).IsEmpty) return true;
                CaptureWindow candidate = new CaptureWindow();
                candidate.Bounds = bounds;
                candidate.Children = ReadChildren(window, bounds, 0, ref childrenRemaining);
                result.Add(candidate);
                return true;
            };
            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return result.ToArray();
        }

        internal static Rectangle FindAt(CaptureWindow[] windows, Point screenPoint, Rectangle virtualScreen)
        {
            if (virtualScreen.Width < 1 || virtualScreen.Height < 1) return Rectangle.Empty;
            if (windows != null)
            {
                foreach (CaptureWindow window in windows)
                {
                    if (window == null) continue;
                    Rectangle bounds = Rectangle.Intersect(window.Bounds, virtualScreen);
                    if (bounds.Width < 1 || bounds.Height < 1 || !bounds.Contains(screenPoint)) continue;
                    return FindChildAt(window, screenPoint, bounds, 0);
                }
            }
            Rectangle monitor = Screen.FromPoint(screenPoint).Bounds;
            Rectangle fallback = Rectangle.Intersect(monitor, virtualScreen);
            return fallback.Width > 0 && fallback.Height > 0 ? fallback : virtualScreen;
        }

        private static Rectangle FindChildAt(CaptureWindow parent, Point point, Rectangle clip, int depth)
        {
            if (depth >= MaxDepth || parent.Children == null) return clip;
            foreach (CaptureWindow child in parent.Children)
            {
                if (child == null) continue;
                Rectangle bounds = Rectangle.Intersect(child.Bounds, clip);
                if (bounds.Width < 16 || bounds.Height < 16 || !bounds.Contains(point)) continue;
                return FindChildAt(child, point, bounds, depth + 1);
            }
            return clip;
        }

        private static CaptureWindow[] ReadChildren(IntPtr parent, Rectangle parentBounds, int depth, ref int remaining)
        {
            if (depth >= MaxDepth || remaining <= 0) return new CaptureWindow[0];
            List<CaptureWindow> result = new List<CaptureWindow>();
            IntPtr child = GetWindow(parent, GW_CHILD);
            // GW_CHILD / GW_HWNDNEXT mantienen el orden Z de los hermanos.
            while (child != IntPtr.Zero && remaining > 0)
            {
                remaining--;
                Rectangle bounds;
                if (IsWindowVisible(child) && TryBounds(child, false, out bounds))
                {
                    bounds = Rectangle.Intersect(bounds, parentBounds);
                    if (bounds.Width >= 16 && bounds.Height >= 16)
                    {
                        CaptureWindow item = new CaptureWindow();
                        item.Bounds = bounds;
                        item.Children = ReadChildren(child, bounds, depth + 1, ref remaining);
                        result.Add(item);
                    }
                }
                child = GetWindow(child, GW_HWNDNEXT);
            }
            return result.ToArray();
        }

        private static bool TryBounds(IntPtr window, bool extendedFrame, out Rectangle bounds)
        {
            NativeRect rect;
            bounds = Rectangle.Empty;
            if (!GetWindowRect(window, out rect)) return false;
            if (extendedFrame)
            {
                try
                {
                    NativeRect visibleFrame;
                    if (GetDwmRectangle(window, 9, out visibleFrame, Marshal.SizeOf(typeof(NativeRect))) == 0)
                        rect = visibleFrame;
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            }
            long width = (long)rect.Right - rect.Left;
            long height = (long)rect.Bottom - rect.Top;
            if (width < 16 || height < 16 || width > Int32.MaxValue || height > Int32.MaxValue) return false;
            bounds = new Rectangle(rect.Left, rect.Top, (int)width, (int)height);
            return true;
        }

        private static bool IsCloaked(IntPtr window)
        {
            try
            {
                int cloaked;
                return GetDwmInteger(window, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        private static bool IsDesktopShell(IntPtr window)
        {
            StringBuilder name = new StringBuilder(128);
            if (GetClassName(window, name, name.Capacity) == 0) return false;
            string value = name.ToString();
            return value == "Progman" || value == "WorkerW" || value == "Shell_TrayWnd" || value == "Shell_SecondaryTrayWnd";
        }
    }
}
