using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NotepadPin
{
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        internal const string WindowTitle = "NotepadPin.MessageWindow.1";
        internal const int AlreadyRunningMessage = 0x8001;
        internal readonly bool HotkeyRegistered;
        private readonly Action toggle;
        private readonly Action close;
        private readonly Action alreadyRunning;

        internal HotkeyWindow(Action toggle, Action close, Action alreadyRunning)
        {
            this.toggle = toggle; this.close = close; this.alreadyRunning = alreadyRunning;
            CreateHandle(new CreateParams { Caption = WindowTitle });
            HotkeyRegistered = Native.RegisterHotKey(Handle, 1, 0x4003, 0x54); // ALT | CTRL | NOREPEAT
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312 && message.WParam.ToInt32() == 1) { toggle(); return; }
            if (message.Msg == 0x0010) { close(); return; }
            if (message.Msg == AlreadyRunningMessage) { alreadyRunning(); return; }
            if (message.Msg == 0x0011) { close(); message.Result = new IntPtr(1); return; }
            base.WndProc(ref message);
        }
        public void Dispose()
        {
            if (Handle == IntPtr.Zero) return;
            if (HotkeyRegistered) Native.UnregisterHotKey(Handle, 1);
            DestroyHandle();
        }
    }

    internal sealed class PinApplication : ApplicationContext
    {
        private readonly NativeWindows windows = new NativeWindows();
        private readonly PinController pins;
        private readonly BorderManager borders;
        private readonly NotifyIcon tray;
        private readonly Icon trayIcon;
        private readonly ContextMenuStrip menu;
        private readonly HotkeyWindow receiver;
        private bool exiting;
        private string menuWarning;
        private string status = "后台运行 · 选中记事本后按 Ctrl+Alt+T";

        internal PinApplication()
        {
            pins = new PinController(windows);
            borders = new BorderManager(windows);
            menu = new ContextMenuStrip();
            trayIcon = TrayArtwork.Create();
            tray = new NotifyIcon { Icon = trayIcon, Text = "记事本置顶 · Ctrl+Alt+T", ContextMenuStrip = menu, Visible = true };
            receiver = new HotkeyWindow(ToggleForeground, TryExit, delegate { });
            menu.Opening += delegate { RebuildMenu(); };
            if (!receiver.HotkeyRegistered)
            {
                SetStatus("快捷键被占用", "可使用托盘菜单；释放 Ctrl+Alt+T 后重启本工具。", true);
            }
            DesktopSession.WriteStatus(receiver.HotkeyRegistered, borders.Count, status, borders.NativeCount);
        }

        private void SetStatus(string title, string text, bool warning = false)
        {
            status = title + "：" + text;
            menuWarning = warning ? title : null;
            string tooltip = "记事本置顶 · " + title;
            tray.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
            DesktopSession.WriteStatus(receiver.HotkeyRegistered, borders.Count, status, borders.NativeCount);
        }

        private void ToggleForeground()
        {
            try
            {
                IntPtr handle = Native.GetForegroundWindow();
                WindowInfo window = windows.Read(handle);
                if (window == null || !window.IsNotepad) return;
                Toggle(handle);
            }
            catch (Win32Exception ex) { ShowFailure(ex); }
            catch (InvalidOperationException ex) { ShowFailure(ex); }
        }

        private void Toggle(IntPtr handle)
        {
            try
            {
                bool pinned = pins.Toggle(handle);
                borders.Track(windows.Read(handle));
                SetStatus(pinned ? "已置顶" : "已取消置顶", "Ctrl+Alt+T 切换；下方菜单勾选表示当前置顶。");
            }
            catch (Win32Exception ex) { ShowFailure(ex); }
            catch (InvalidOperationException ex) { ShowFailure(ex); }
        }

        private void ShowFailure(Exception ex)
        {
            SetStatus("操作失败", ex.Message.Replace("\r", " ").Replace("\n", " ") + " 若记事本以管理员身份运行，请以管理员身份启动本工具。", true);
        }

        private void RebuildMenu()
        {
            // Explicitly dispose old items; clearing alone leaves native resources alive.
            while (menu.Items.Count > 0) { ToolStripItem item = menu.Items[0]; menu.Items.RemoveAt(0); item.Dispose(); }
            string warning = receiver.HotkeyRegistered ? menuWarning : "快捷键被占用";
            if (warning != null)
            {
                menu.Items.Add(new ToolStripMenuItem(warning) { Enabled = false, ToolTipText = status });
                menu.Items.Add(new ToolStripSeparator());
            }
            int inaccessible;
            var list = windows.ListNotepads(out inaccessible);
            foreach (WindowInfo window in list)
            {
                WindowInfo captured = window;
                string title = string.IsNullOrWhiteSpace(window.Title) ? "记事本" : window.Title;
                foreach (string suffix in new[] { " - 记事本", " - Notepad" })
                    if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    { title = title.Substring(0, title.Length - suffix.Length); break; }
                if (title.Length > 70) title = title.Substring(0, 67) + "…";
                ToolStripMenuItem item = new ToolStripMenuItem(title.Replace("&", "&&")) { Checked = window.IsTopmost };
                item.ToolTipText = window.Title;
                item.Click += delegate
                {
                    // Refresh identity; a stale menu item must never affect a reused handle.
                    try
                    {
                        if (!captured.SameWindow(windows.Read(captured.Handle)))
                        { SetStatus("窗口已关闭", "请重新打开托盘菜单并选择记事本。", true); return; }
                        Toggle(captured.Handle);
                    }
                    catch (Win32Exception ex) { ShowFailure(ex); }
                    catch (InvalidOperationException ex) { ShowFailure(ex); }
                };
                menu.Items.Add(item);
            }
            if (list.Count == 0) menu.Items.Add(new ToolStripMenuItem("未打开记事本") { Enabled = false });
            if (inaccessible > 0) menu.Items.Add(new ToolStripMenuItem("部分窗口权限不足") { Enabled = false, ToolTipText = "请尝试以管理员身份运行" });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("取消全部置顶", null, delegate
            {
                int remaining = pins.Restore();
                borders.Refresh();
                SetStatus(remaining == 0 ? "已取消全部置顶" : "部分窗口未能恢复", remaining == 0 ? "其他工具设置的置顶保持原状。" : "请检查窗口权限后重试。", remaining != 0);
            });
            menu.Items.Add("退出", null, delegate { TryExit(); });
        }

        private void TryExit()
        {
            if (exiting) return;
            int remaining = pins.Restore();
            borders.Refresh();
            if (remaining > 0)
            {
                SetStatus("退出恢复未完成", "仍有 " + remaining + " 个窗口未能取消置顶。工具继续后台运行；检查权限后再次点击退出可重试。", true);
                return;
            }
            exiting = true;
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            tray.Visible = false;
            receiver.Dispose();
            tray.Dispose();
            trayIcon.Dispose();
            menu.Dispose();
            borders.Dispose();
            windows.Dispose();
            base.ExitThreadCore();
        }
    }

    internal static class Program
    {
        private static void LogError(Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "notepad-pin.log"),
                    DateTime.Now.ToString("s") + " " + ex + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        [STAThread]
        private static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { LogError(e.Exception); };
                bool created;
                using (Mutex mutex = new Mutex(true, DesktopSession.MutexNameFor(DesktopSession.CurrentName), out created))
                {
                    if (!created) return;
                    try { Application.Run(new PinApplication()); }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
                Environment.ExitCode = 1;
            }
        }
    }
}
