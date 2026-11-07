using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NotepadPin
{
    internal sealed class BorderManager : IDisposable
    {
        private sealed class Entry
        {
            internal WindowInfo Target;
            internal PinBorder Frame;
            internal NativePinBorder NativeFrame;
        }
        private readonly IWindows windows;
        private readonly Dictionary<IntPtr, Entry> entries = new Dictionary<IntPtr, Entry>();
        private readonly Timer timer;
        internal int Count { get { return entries.Count; } }
        internal int NativeCount
        {
            get
            {
                int count = 0;
                foreach (Entry entry in entries.Values) if (entry.NativeFrame != null) count++;
                return count;
            }
        }

        internal BorderManager(IWindows windows)
        {
            this.windows = windows;
            timer = new Timer { Interval = 100 };
            timer.Tick += delegate { Refresh(); };
            timer.Start();
        }

        internal void Track(WindowInfo window)
        {
            if (window == null) return;
            Entry entry;
            if (entries.TryGetValue(window.Handle, out entry) && !entry.Target.SameWindow(window)) Remove(window.Handle);
            if (window.IsTopmost && !entries.ContainsKey(window.Handle))
            {
                NativePinBorder native = NativePinBorder.TryCreate(windows, window);
                entries[window.Handle] = new Entry { Target = window, NativeFrame = native,
                    Frame = native == null ? new PinBorder(window.Handle) : null };
            }
            Refresh();
        }

        internal void Refresh()
        {
            foreach (Entry entry in new List<Entry>(entries.Values))
            {
                try
                {
                    WindowInfo current = windows.Read(entry.Target.Handle);
                    if (!entry.Target.SameWindow(current) || !current.IsTopmost)
                    { Remove(entry.Target.Handle); continue; }
                    // Native borders belong to the target window itself. This timer
                    // only detects unpins/closed targets, never moves a native border.
                    if (entry.Frame != null) entry.Frame.Follow();
                }
                catch (Win32Exception) { if (entry.Frame != null) entry.Frame.Hide(); }
                catch (InvalidOperationException) { if (entry.Frame != null) entry.Frame.Hide(); }
            }
        }

        private void Remove(IntPtr handle)
        {
            Entry entry;
            if (!entries.TryGetValue(handle, out entry)) return;
            entries.Remove(handle);
            if (entry.Frame != null) entry.Frame.Dispose();
            if (entry.NativeFrame != null) entry.NativeFrame.Dispose();
        }

        public void Dispose()
        {
            timer.Stop(); timer.Dispose();
            foreach (IntPtr handle in new List<IntPtr>(entries.Keys)) Remove(handle);
        }
    }

    internal sealed class NativePinBorder : IDisposable
    {
        private const uint PinColor = 0x00DCAF00; // COLORREF: cyan RGB(0,175,220)
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int GetColor(IntPtr handle, int attribute, out uint value, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
        private static extern int SetColor(IntPtr handle, int attribute, ref uint value, int size);
        private readonly IWindows windows;
        private readonly WindowInfo target;
        private readonly uint originalColor;

        private NativePinBorder(IWindows windows, WindowInfo target, uint originalColor)
        { this.windows = windows; this.target = target; this.originalColor = originalColor; }

        internal static NativePinBorder TryCreate(IWindows windows, WindowInfo target)
        {
            uint original;
            // DWMWA_BORDER_COLOR is a documented setter attribute. Some builds
            // accept Set but reject Get; that must not select the slow overlay.
            if (GetColor(target.Handle, 34, out original, 4) != 0) original = 0xFFFFFFFF;
            uint color = PinColor;
            if (SetColor(target.Handle, 34, ref color, 4) != 0) return null;
            return new NativePinBorder(windows, target, original);
        }

        public void Dispose()
        {
            try
            {
                // Never restore a different window after HWND reuse, or overwrite
                // a new border color explicitly chosen by another app/tool.
                if (!target.SameWindow(windows.Read(target.Handle))) return;
                uint current;
                if (GetColor(target.Handle, 34, out current, 4) == 0 && current != PinColor) return;
                uint restore = originalColor;
                SetColor(target.Handle, 34, ref restore, 4);
            }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
        }
    }

    internal sealed class PinBorder : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out Rect rect);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr handle);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int GetFrameBounds(IntPtr handle, int attribute, out Rect rect, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int GetCloaked(IntPtr handle, int attribute, out int value, int size);
        private readonly IntPtr target;

        internal PinBorder(IntPtr target)
        {
            this.target = target;
            Text = "NotepadPin.Border." + target;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(0, 175, 220);
            Opacity = 0.90;
            TopMost = true;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x080800A0; // NOACTIVATE | LAYERED | TOOLWINDOW | TRANSPARENT
                return parameters;
            }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; } // HTTRANSPARENT
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            base.WndProc(ref message);
        }

        internal void Follow()
        {
            int cloaked;
            if (!Native.IsWindowVisible(target) || IsIconic(target)
                || (GetCloaked(target, 14, out cloaked, 4) == 0 && cloaked != 0))
            { Hide(); return; }
            Rect bounds;
            if (GetFrameBounds(target, 9, out bounds, Marshal.SizeOf(typeof(Rect))) != 0 && !GetWindowRect(target, out bounds))
            { Hide(); return; }
            Rectangle rectangle = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            if (rectangle.Width < 8 || rectangle.Height < 8) { Hide(); return; }
            if (!IsZoomed(target)) rectangle.Inflate(2, 2);
            if (Bounds != rectangle)
            {
                Bounds = rectangle;
                Region ring = new Region(new Rectangle(0, 0, Width, Height));
                ring.Exclude(new Rectangle(3, 3, Width - 6, Height - 6));
                Region previous = Region;
                Region = ring;
                if (previous != null) previous.Dispose();
            }
            if (!Visible) Show();
            // Insert immediately above the target, rather than above unrelated topmost windows.
            IntPtr above = Native.GetWindow(target, 3); // GW_HWNDPREV
            uint flags = 0x13; // NOSIZE | NOMOVE | NOACTIVATE
            if (above == Handle) flags |= 4; // already adjacent; keep z order
            Native.SetWindowPos(Handle, above == IntPtr.Zero ? new IntPtr(-1) : above, 0, 0, 0, 0, flags);
        }
    }
}
