using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using NotepadPin;

internal static class Integration
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    private static int count;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        Console.WriteLine("PASS: " + label); count++;
    }
    private static bool Until(Func<bool> predicate, int milliseconds)
    {
        Stopwatch watch = Stopwatch.StartNew();
        do { if (predicate()) return true; Thread.Sleep(80); } while (watch.ElapsedMilliseconds < milliseconds);
        return false;
    }
    private static void Hotkey()
    {
        keybd_event(0x11, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x54, 0, 0, UIntPtr.Zero);
        keybd_event(0x54, 0, 2, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        keybd_event(0x11, 0, 2, UIntPtr.Zero);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        bool externalInput = args.Length > 1 && args[1] == "external-input";
        bool nativeOnly = args.Length > 1 && args[1] == "native-only";
        Process app = null;
        IntPtr createdWindow = IntPtr.Zero;
        HashSet<IntPtr> baseline = new HashSet<IntPtr>();
        string file = Path.Combine(Path.GetTempPath(), "NotepadPin-test-" + Guid.NewGuid().ToString("N") + ".txt");
        IntPtr originalForeground = Native.GetForegroundWindow();
        try
        {
            using (NativeWindows windows = new NativeWindows())
            {
                int inaccessible;
                foreach (WindowInfo existing in windows.ListNotepads(out inaccessible)) baseline.Add(existing.Handle);
                // Avoid opening a tab in an existing user window.
                if (baseline.Count != 0 || inaccessible != 0)
                    throw new Exception("Real Notepad integration skipped: existing user windows must be preserved.");
                File.WriteAllText(file, "");
                using (Process launch = Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe"), "\"" + file + "\"")) { }
                Check(Until(delegate
                {
                    foreach (WindowInfo window in windows.ListNotepads(out inaccessible))
                        if (!baseline.Contains(window.Handle) && window.Title.Contains(Path.GetFileNameWithoutExtension(file)))
                        { createdWindow = window.Handle; return true; }
                    return false;
                }, 12000), "disposable real Notepad window opens");
                WindowInfo before = windows.Read(createdWindow);
                Check(before != null && before.IsNotepad && !before.IsTopmost, "real Notepad is identified and starts unpinned");

                app = Process.Start(new ProcessStartInfo(args[0]) { UseShellExecute = false, CreateNoWindow = true });
                Check(Until(delegate { return FindWindow(null, HotkeyWindow.WindowTitle) != IntPtr.Zero; }, 5000), "release EXE starts its tray message loop");
                using (Process duplicate = Process.Start(new ProcessStartInfo(args[0]) { UseShellExecute = false, CreateNoWindow = true }))
                    Check(duplicate.WaitForExit(3000), "duplicate instance exits promptly");
                Check(!app.HasExited, "first instance remains running");
                if (nativeOnly)
                {
                    PinController controller = new PinController(windows);
                    Check(controller.Toggle(createdWindow) && windows.Read(createdWindow).IsTopmost, "controller pins real Notepad through native API");
                    Check(!controller.Toggle(createdWindow) && !windows.Read(createdWindow).IsTopmost, "controller unpins real Notepad through native API");
                    controller.Toggle(createdWindow);
                    Check(controller.Restore() == 0 && !windows.Read(createdWindow).IsTopmost, "controller restores real Notepad");
                    PostMessage(FindWindow(null, HotkeyWindow.WindowTitle), 0x0010, IntPtr.Zero, IntPtr.Zero);
                    Check(app.WaitForExit(5000), "normal close exits release EXE");
                    windows.SetTopmost(windows.Read(createdWindow), true);
                    Check(controller.Restore() == 0 && windows.Read(createdWindow).IsTopmost, "real preexisting topmost state is preserved");
                    windows.SetTopmost(windows.Read(createdWindow), false);
                    PostMessage(createdWindow, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    Check(Until(delegate { return !Native.IsWindow(createdWindow); }, 3000), "only disposable Notepad window closes");
                    createdWindow = IntPtr.Zero;
                    Console.WriteLine("RESULT: " + count + " native integration checks passed, 0 failed");
                    Console.WriteLine("LIMIT: real foreground keyboard input not verified in native-only mode.");
                    return 0;
                }
                SetForegroundWindow(createdWindow);
                Console.WriteLine("WAIT: focus disposable Notepad, HWND=" + createdWindow);
                Check(Until(delegate { return Native.GetForegroundWindow() == createdWindow; }, externalInput ? 45000 : 3000), "test Notepad receives foreground focus");
                Console.WriteLine("WAIT: press Ctrl+Alt+T to pin");
                if (!externalInput) Hotkey();
                Check(Until(delegate { return windows.Read(createdWindow).IsTopmost; }, externalInput ? 45000 : 3000), "actual Ctrl+Alt+T pins real Notepad");
                Console.WriteLine("WAIT: press Ctrl+Alt+T to unpin");
                if (!externalInput) Hotkey();
                Check(Until(delegate { return !windows.Read(createdWindow).IsTopmost; }, externalInput ? 45000 : 3000), "actual Ctrl+Alt+T unpins real Notepad");
                Console.WriteLine("WAIT: press Ctrl+Alt+T to repin");
                if (!externalInput) Hotkey();
                Check(Until(delegate { return windows.Read(createdWindow).IsTopmost; }, externalInput ? 45000 : 3000), "actual shortcut can repin");
                IntPtr appWindow = FindWindow(null, HotkeyWindow.WindowTitle);
                PostMessage(appWindow, 0x0010, IntPtr.Zero, IntPtr.Zero);
                Check(app.WaitForExit(5000), "normal close exits release EXE");
                Check(!windows.Read(createdWindow).IsTopmost, "normal exit restores real Notepad");
                // Closing a blank, saved test file must not prompt to save.
                PostMessage(createdWindow, 0x0010, IntPtr.Zero, IntPtr.Zero);
                Check(Until(delegate { return !Native.IsWindow(createdWindow); }, 3000), "only disposable Notepad window closes");
                createdWindow = IntPtr.Zero;
            }
            Console.WriteLine("RESULT: " + count + " integration checks passed, 0 failed");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            if (app != null)
            {
                if (!app.HasExited)
                {
                    PostMessage(FindWindow(null, HotkeyWindow.WindowTitle), 0x0010, IntPtr.Zero, IntPtr.Zero);
                    if (!app.WaitForExit(3000)) app.Kill();
                }
                app.Dispose();
            }
            if (createdWindow != IntPtr.Zero && !baseline.Contains(createdWindow))
                PostMessage(createdWindow, 0x0010, IntPtr.Zero, IntPtr.Zero);
            if (originalForeground != IntPtr.Zero) SetForegroundWindow(originalForeground);
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
