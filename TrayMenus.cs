using System.Runtime.InteropServices;

namespace CodexLimitViewer;

internal sealed class TrayContextMenu : ContextMenuStrip
{
    private readonly System.Windows.Forms.Timer outsideClicks = new() { Interval = 15 };
    private readonly LowLevelMouseProc mouseProc;
    private IntPtr mouseHook;
    private bool wasPressed;
    private bool closeForAction;
    private bool outsideClickQueued;
    internal Func<Rectangle>? TrayAnchor;
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, LowLevelMouseProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? moduleName);
    private delegate IntPtr LowLevelMouseProc(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct MouseHookInfo
    {
        public Point Point;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }
    internal TrayContextMenu()
    {
        mouseProc = OnMouseHook;
        AutoClose = true;
        outsideClicks.Tick += (_, _) => ObservePointer(Cursor.Position, Pressed());
    }
    private IntPtr OnMouseHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && Visible && !outsideClickQueued &&
            message.ToInt64() is 0x201 or 0x204 or 0x207)
        {
            var point = Marshal.PtrToStructure<MouseHookInfo>(data).Point;
            if (!ContainsMenu(this, point))
            {
                outsideClickQueued = true;
                BeginInvoke(() =>
                {
                    outsideClickQueued = false;
                    if (Visible) Close(ToolStripDropDownCloseReason.AppClicked);
                });
            }
        }
        return CallNextHookEx(mouseHook, code, message, data);
    }
    private static bool Pressed() => (GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0;
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (TrayAnchor is { } anchor)
        {
            var bounds = anchor();
            Location = TrayPresentation.Above(bounds, Size, Screen.FromRectangle(bounds).WorkingArea);
        }
        wasPressed = Pressed();
        outsideClicks.Start();
        if (mouseHook == IntPtr.Zero) mouseHook = SetWindowsHookEx(14, mouseProc, GetModuleHandle(null), 0);
    }
    internal void ObservePointer(Point point, bool pressed)
    {
        if (pressed && !wasPressed && !ContainsMenu(this, point)) Close(ToolStripDropDownCloseReason.AppClicked);
        wasPressed = pressed;
    }
    internal void CloseForAction()
    {
        closeForAction = true;
        try { Close(); }
        finally { closeForAction = false; }
    }
    private static bool ContainsMenu(ToolStripDropDown menu, Point point)
    {
        if (menu.Visible && menu.Bounds.Contains(point)) return true;
        return menu.Items.OfType<ToolStripDropDownItem>().Any(item => item.HasDropDownItems && item.DropDown.Visible && ContainsMenu(item.DropDown, point));
    }
    protected override void OnClosing(ToolStripDropDownClosingEventArgs e)
    {
        if (!closeForAction && e.CloseReason != ToolStripDropDownCloseReason.Keyboard &&
            ContainsMenu(this, Cursor.Position)) e.Cancel = true;
        base.OnClosing(e);
    }
    protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
    {
        outsideClicks.Stop();
        if (mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        base.OnClosed(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            outsideClicks.Dispose();
            if (mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        }
        base.Dispose(disposing);
    }
    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle = (p.ExStyle | 0x80) & ~0x40000; // TOOLWINDOW, never APPWINDOW
            return p;
        }
    }
}

internal sealed class TraySubMenu : ToolStripDropDownMenu
{
    protected override void OnClosing(ToolStripDropDownClosingEventArgs e)
    {
        if (e.CloseReason != ToolStripDropDownCloseReason.Keyboard && Visible && Bounds.Contains(Cursor.Position)) e.Cancel = true;
        base.OnClosing(e);
    }
    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle = (p.ExStyle | 0x80) & ~0x40000;
            return p;
        }
    }
}
