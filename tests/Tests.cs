using System;
using System.Collections.Generic;
using System.Windows.Forms;
using NotepadPin;

internal static class Tests
{
    private static int count;
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("FAIL: " + name);
        count++;
        Console.WriteLine("PASS: " + name);
    }

    // Only the external window API is substituted; ownership logic is real.
    private sealed class Desktop : IWindows
    {
        internal readonly Dictionary<IntPtr, WindowInfo> Items = new Dictionary<IntPtr, WindowInfo>();
        internal bool Fail;
        internal void Add(int handle, bool pinned, bool notepad, long identity)
        {
            Items[new IntPtr(handle)] = new WindowInfo(new IntPtr(handle), 42, identity, notepad, "sample", pinned);
        }
        public WindowInfo Read(IntPtr handle)
        {
            WindowInfo value;
            return Items.TryGetValue(handle, out value) ? value : null;
        }
        public void SetTopmost(WindowInfo value, bool pinned)
        {
            if (Fail) throw new InvalidOperationException("simulated access denied");
            WindowInfo current = Read(value.Handle);
            if (current == null || !current.SameWindow(value)) throw new InvalidOperationException("stale window");
            Add(value.Handle.ToInt32(), pinned, value.IsNotepad, value.Identity);
        }
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            Desktop desktop = new Desktop();
            PinController pins = new PinController(desktop);
            IntPtr one = new IntPtr(1), two = new IntPtr(2);
            desktop.Add(1, false, true, 100);
            Check(pins.Toggle(one) && desktop.Read(one).IsTopmost, "toggle pins an ordinary Notepad");
            Check(!pins.Toggle(one) && !desktop.Read(one).IsTopmost, "second toggle unpins");
            desktop.Add(2, false, true, 200);
            pins.Toggle(one); pins.Toggle(two);
            Check(pins.Restore() == 0 && !desktop.Read(one).IsTopmost && !desktop.Read(two).IsTopmost, "exit restores multiple owned windows");
            desktop.Add(1, true, true, 100);
            Check(pins.Restore() == 0 && desktop.Read(one).IsTopmost, "preexisting pin is preserved");
            pins.Toggle(one);
            Check(!desktop.Read(one).IsTopmost && pins.Restore() == 0, "explicit toggle can remove a preexisting pin");
            desktop.Add(1, false, false, 100);
            bool rejected = false;
            try { pins.Toggle(one); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !desktop.Read(one).IsTopmost, "non-Notepad is rejected");
            desktop.Add(1, false, true, 100);
            pins.Toggle(one);
            desktop.Items.Remove(one);
            Check(pins.Restore() == 0, "closed windows are ignored");
            desktop.Add(1, false, true, 100);
            pins.Toggle(one);
            desktop.Add(1, true, true, 101);
            Check(pins.Restore() == 0 && desktop.Read(one).IsTopmost, "reused handle with new identity is preserved");
            desktop.Add(1, false, true, 102);
            desktop.Fail = true;
            bool failed = false;
            try { pins.Toggle(one); } catch (InvalidOperationException) { failed = true; }
            Check(failed && !desktop.Read(one).IsTopmost, "failed pin leaves state unchanged");
            desktop.Fail = false;
            pins.Toggle(one);
            desktop.Fail = true;
            Check(pins.Restore() == 1 && desktop.Read(one).IsTopmost, "failed restoration is reported");
            desktop.Fail = false;
            Check(pins.Restore() == 0 && !desktop.Read(one).IsTopmost, "failed restoration can be retried");
            pins.Toggle(one);
            desktop.Fail = true;
            failed = false;
            try { pins.Toggle(one); } catch (InvalidOperationException) { failed = true; }
            desktop.Fail = false;
            Check(failed && pins.Restore() == 0 && !desktop.Read(one).IsTopmost, "failed unpin retains exit ownership");
            Check(pins.Restore() == 0, "restoration is idempotent");

            bool mutexCreated;
            string desktopSuffix = Guid.NewGuid().ToString("N");
            using (var sandboxMutex = new System.Threading.Mutex(false, DesktopSession.MutexNameFor("CodexSandboxDesktop-test-" + desktopSuffix), out mutexCreated))
            using (var userMutex = new System.Threading.Mutex(false, DesktopSession.MutexNameFor("User-test-" + desktopSuffix), out mutexCreated))
            {
                Check(mutexCreated, "an isolated desktop instance cannot block the user desktop");
                using (var duplicateMutex = new System.Threading.Mutex(false, DesktopSession.MutexNameFor("User-test-" + desktopSuffix), out mutexCreated))
                    Check(!mutexCreated, "duplicate launches on the same desktop share a mutex");
            }

            using (NativeWindows native = new NativeWindows())
            using (Form window = new Form())
            {
                window.Text = "NotepadPin disposable native test";
                window.ShowInTaskbar = false;
                window.Show();
                WindowInfo before = native.Read(window.Handle);
                Check(before != null && !before.IsNotepad && !before.IsTopmost, "native backend identifies non-Notepad");
                native.SetTopmost(before, true);
                Check(native.Read(window.Handle).IsTopmost, "native topmost style is set");
                uint originalColor;
                bool nativeColorReadable = TestMessages.GetBorderColor(window.Handle, 34, out originalColor, 4) == 0;
                uint defaultColor = 0xFFFFFFFF;
                bool nativeBorderSupported = TestMessages.SetBorderColor(window.Handle, 34, ref defaultColor, 4) == 0;
                uint customColor = 0x00443322;
                if (nativeBorderSupported && nativeColorReadable)
                    Check(TestMessages.SetBorderColor(window.Handle, 34, ref customColor, 4) == 0, "test window accepts a custom original border color");
                using (BorderManager borders = new BorderManager(native))
                {
                    borders.Track(native.Read(window.Handle));
                    Check(borders.Count == 1, "a pinned window receives one border");
                    borders.Track(native.Read(window.Handle));
                    Check(borders.Count == 1, "refreshing a pin does not duplicate its border");
                    IntPtr overlay = TestMessages.FindWindow(null, "NotepadPin.Border." + window.Handle);
                    if (nativeBorderSupported)
                    {
                        Check(borders.NativeCount == 1 && overlay == IntPtr.Zero, "supported Windows uses its own border with no following overlay");
                        uint color;
                        if (nativeColorReadable)
                        {
                            TestMessages.GetBorderColor(window.Handle, 34, out color, 4);
                            Check(color == 0x00DCAF00, "native border displays cyan");
                        }
                        window.Bounds = new System.Drawing.Rectangle(100, 100, 420, 280);
                        // No manager refresh or timer tick: the indicator belongs to the target itself.
                        Check(borders.NativeCount == 1 && TestMessages.FindWindow(null, "NotepadPin.Border." + window.Handle) == IntPtr.Zero,
                            "move and resize need no following-window refresh");
                    }
                    else
                    {
                        long style = Native.GetExtendedStyle(overlay).ToInt64();
                        Check(overlay != IntPtr.Zero && (style & 0x08000020) == 0x08000020, "fallback border is click-through and does not activate");
                        Check(Native.GetForegroundWindow() != overlay, "fallback border never takes foreground focus");
                    }
                    native.SetTopmost(native.Read(window.Handle), false);
                    borders.Refresh();
                    Check(borders.Count == 0 && !Native.IsWindow(overlay), "unpinning removes and disposes the border");
                    if (nativeBorderSupported && nativeColorReadable)
                    {
                        uint color;
                        TestMessages.GetBorderColor(window.Handle, 34, out color, 4);
                        Check(color == customColor, "unpinning restores the original native border color");
                    }
                    native.SetTopmost(native.Read(window.Handle), true);
                    borders.Track(native.Read(window.Handle));
                    overlay = TestMessages.FindWindow(null, "NotepadPin.Border." + window.Handle);
                    window.Close();
                    borders.Refresh();
                    Check(borders.Count == 0 && !Native.IsWindow(overlay), "closing a target removes the border");
                }
                Check(!Native.IsWindow(before.Handle), "native test window is closed");
            }
            int requests = 0;
            using (HotkeyWindow first = new HotkeyWindow(delegate { requests++; }, delegate { }, delegate { }))
            {
                Check(first.HotkeyRegistered, "Ctrl+Alt+T registers successfully");
                using (HotkeyWindow second = new HotkeyWindow(delegate { }, delegate { }, delegate { }))
                    Check(!second.HotkeyRegistered, "hotkey conflict is detected without crashing");
                Message message = Message.Create(first.Handle, 0x0312, new IntPtr(1), IntPtr.Zero);
                TestMessages.Send(first.Handle, message.Msg, message.WParam, message.LParam);
                Check(requests == 1, "hotkey message dispatches exactly one toggle");
                TestMessages.Send(first.Handle, 0x0312, new IntPtr(999), IntPtr.Zero);
                Check(requests == 1, "unrelated hotkey messages are ignored");
            }
            using (HotkeyWindow next = new HotkeyWindow(delegate { }, delegate { }, delegate { }))
                Check(next.HotkeyRegistered, "exit releases the hotkey for restart");
            Console.WriteLine("RESULT: " + count + " passed, 0 failed");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static class TestMessages
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        internal static extern IntPtr FindWindow(string className, string title);
        [System.Runtime.InteropServices.DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        internal static extern int GetBorderColor(IntPtr handle, int attribute, out uint color, int size);
        [System.Runtime.InteropServices.DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
        internal static extern int SetBorderColor(IntPtr handle, int attribute, ref uint color, int size);
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
        internal static extern IntPtr Send(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    }
}
