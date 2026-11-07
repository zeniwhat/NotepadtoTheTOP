using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NotepadPin
{
    internal sealed class WindowInfo
    {
        internal readonly IntPtr Handle;
        internal readonly int ProcessId;
        internal readonly long Identity;
        internal readonly bool IsNotepad;
        internal readonly string Title;
        internal readonly bool IsTopmost;

        internal WindowInfo(IntPtr handle, int processId, long identity, bool notepad, string title, bool topmost)
        {
            Handle = handle; ProcessId = processId; Identity = identity;
            IsNotepad = notepad; Title = title; IsTopmost = topmost;
        }
        internal bool SameWindow(WindowInfo other)
        {
            return other != null && Handle == other.Handle && ProcessId == other.ProcessId && Identity == other.Identity;
        }
    }

    internal interface IWindows
    {
        WindowInfo Read(IntPtr handle);
        void SetTopmost(WindowInfo window, bool topmost);
    }

    internal sealed class NativeWindows : IWindows, IDisposable
    {
        // Window properties disappear when the window is destroyed, including
        // when a handle is immediately reused inside the same Notepad process.
        private readonly string propertyName = "NotepadPin." + Guid.NewGuid().ToString("N");
        private readonly Dictionary<IntPtr, IntPtr> markers = new Dictionary<IntPtr, IntPtr>();
        private int nextMarker;

        public WindowInfo Read(IntPtr handle)
        {
            if (handle == IntPtr.Zero || !Native.IsWindow(handle)) return null;
            uint pid;
            Native.GetWindowThreadProcessId(handle, out pid);
            if (pid == 0) return null;
            bool notepad;
            long identity;
            try
            {
                using (Process process = Process.GetProcessById((int)pid))
                {
                    notepad = string.Equals(process.ProcessName, "notepad", StringComparison.OrdinalIgnoreCase)
                        && Native.GetAncestor(handle, 2) == handle;
                    identity = process.StartTime.ToUniversalTime().Ticks;
                }
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            if (notepad)
            {
                IntPtr marker = Native.GetProp(handle, propertyName);
                if (marker == IntPtr.Zero)
                {
                    marker = new IntPtr(checked(++nextMarker));
                    if (!Native.SetProp(handle, propertyName, marker)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    markers[handle] = marker;
                }
                identity = marker.ToInt64();
            }
            StringBuilder title = new StringBuilder(1024);
            Native.GetWindowText(handle, title, title.Capacity);
            return new WindowInfo(handle, (int)pid, identity, notepad, title.ToString(),
                (Native.GetExtendedStyle(handle).ToInt64() & 8) != 0);
        }

        public void SetTopmost(WindowInfo window, bool topmost)
        {
            WindowInfo current = Read(window.Handle);
            if (!window.SameWindow(current)) throw new InvalidOperationException("窗口已关闭或发生变化，请重新选择。");
            // NOSIZE | NOMOVE | NOACTIVATE: preserve size, position and focus.
            if (!Native.SetWindowPos(window.Handle, new IntPtr(topmost ? -1 : -2), 0, 0, 0, 0, 0x13))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            current = Read(window.Handle);
            if (!window.SameWindow(current) || current.IsTopmost != topmost)
                throw new InvalidOperationException("窗口状态未能更改。若记事本以管理员身份运行，请以管理员身份启动本工具。");
        }

        internal List<WindowInfo> ListNotepads(out int inaccessible)
        {
            List<WindowInfo> windows = new List<WindowInfo>();
            int failures = 0;
            Native.EnumWindows(delegate(IntPtr handle, IntPtr unused)
            {
                if (!Native.IsWindowVisible(handle) || Native.GetWindow(handle, 4) != IntPtr.Zero) return true;
                // Filter before Read to avoid querying every app's process start time.
                uint pid;
                Native.GetWindowThreadProcessId(handle, out pid);
                try
                {
                    using (Process process = Process.GetProcessById((int)pid))
                        if (!string.Equals(process.ProcessName, "notepad", StringComparison.OrdinalIgnoreCase)) return true;
                    WindowInfo window = Read(handle);
                    if (window != null && window.IsNotepad) windows.Add(window);
                }
                catch (Win32Exception) { failures++; }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                return true;
            }, IntPtr.Zero);
            inaccessible = failures;
            return windows;
        }

        public void Dispose()
        {
            foreach (KeyValuePair<IntPtr, IntPtr> marker in markers)
                if (Native.GetProp(marker.Key, propertyName) == marker.Value)
                    Native.RemoveProp(marker.Key, propertyName);
            markers.Clear();
        }
    }

    internal static class Native
    {
        internal delegate bool EnumProc(IntPtr handle, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr handle);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr handle, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr handle, uint command);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr handle, StringBuilder title, int length);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SetProp(IntPtr handle, string name, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetProp(IntPtr handle, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr RemoveProp(IntPtr handle, string name);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr handle, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLong64(IntPtr handle, int index);
        internal static IntPtr GetExtendedStyle(IntPtr handle) { return IntPtr.Size == 8 ? GetWindowLong64(handle, -20) : new IntPtr(GetWindowLong32(handle, -20)); }
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr handle, int id);
    }
}
