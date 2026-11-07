using System;
using System.Collections.Generic;

namespace NotepadPin
{
    internal sealed class PinController
    {
        private readonly IWindows windows;
        private readonly Dictionary<IntPtr, WindowInfo> owned = new Dictionary<IntPtr, WindowInfo>();
        internal PinController(IWindows windows) { this.windows = windows; }

        internal bool Toggle(IntPtr handle)
        {
            WindowInfo current = windows.Read(handle);
            if (current == null || !current.IsNotepad) throw new InvalidOperationException("请先选中一个记事本窗口。");
            bool topmost = !current.IsTopmost;
            windows.SetTopmost(current, topmost);
            if (topmost) owned[handle] = current;
            else owned.Remove(handle);
            return topmost;
        }

        // Returns the number of windows that still need restoration.
        internal int Restore()
        {
            foreach (WindowInfo original in new List<WindowInfo>(owned.Values))
            {
                try
                {
                    WindowInfo current = windows.Read(original.Handle);
                    if (original.SameWindow(current) && current.IsNotepad && current.IsTopmost)
                        windows.SetTopmost(current, false);
                    owned.Remove(original.Handle);
                }
                catch (Exception ex)
                {
                    if (!(ex is System.ComponentModel.Win32Exception) && !(ex is InvalidOperationException)) throw;
                    // Retain ownership so that a normal exit can be retried.
                }
            }
            return owned.Count;
        }
    }
}
