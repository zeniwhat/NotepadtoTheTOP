using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NotepadPin
{
    internal static class DesktopSession
    {
        [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint threadId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder name, uint length, out uint needed);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);

        internal static string CurrentName
        {
            get
            {
                StringBuilder name = new StringBuilder(256);
                uint needed;
                if (!GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, 512, out needed))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                return name.ToString();
            }
        }

        internal static string MutexNameFor(string desktop)
        {
            // Local\ scopes to session; the encoded suffix scopes to desktop.
            return "Local\\NotepadPin.Tray.2." + Convert.ToBase64String(Encoding.UTF8.GetBytes(desktop)).Replace('/', '_');
        }

        internal static void WriteStatus(bool hotkeyRegistered, int borders, string status, int nativeBorders = 0)
        {
            try
            {
                string text = "ProcessId: " + Process.GetCurrentProcess().Id + Environment.NewLine
                    + "Desktop: " + CurrentName + Environment.NewLine
                    + "ShellTrayDetected: " + (FindWindow("Shell_TrayWnd", null) != IntPtr.Zero) + Environment.NewLine
                    + "HotkeyRegistered: " + hotkeyRegistered + Environment.NewLine
                    + "PinnedBorders: " + borders + Environment.NewLine
                    + "NativeBorders: " + nativeBorders + Environment.NewLine
                    + "Status: " + status + Environment.NewLine
                    + "Updated: " + DateTime.Now.ToString("s") + Environment.NewLine;
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "notepad-pin.status.txt"), text, Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static class TrayArtwork
    {
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        internal static Icon Create()
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                using (Brush background = new SolidBrush(Color.FromArgb(0, 130, 175)))
                    graphics.FillEllipse(background, 1, 1, 30, 30);
                using (Pen pin = new Pen(Color.White, 2.4f))
                {
                    pin.StartCap = LineCap.Round; pin.EndCap = LineCap.Round;
                    graphics.DrawLine(pin, 12, 8, 22, 12);
                    graphics.DrawLine(pin, 14, 9, 11, 16);
                    graphics.DrawLine(pin, 20, 12, 17, 19);
                    graphics.DrawLine(pin, 9, 16, 19, 20);
                    graphics.DrawLine(pin, 14, 19, 11, 26);
                }
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
    }
}
