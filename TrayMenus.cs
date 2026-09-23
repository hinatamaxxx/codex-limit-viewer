using System.Runtime.InteropServices;

namespace CodexLimitViewer;

internal sealed class TrayContextMenu : ContextMenuStrip
{
    private readonly System.Windows.Forms.Timer outsideClicks = new() { Interval = 15 };
    private bool wasPressed;
    internal Func<Rectangle>? TrayAnchor;
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    internal TrayContextMenu()
    {
        AutoClose = false;
        outsideClicks.Tick += (_, _) => ObservePointer(Cursor.Position, Pressed());
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
    }
    internal void ObservePointer(Point point, bool pressed)
    {
        if (pressed && !wasPressed && !ContainsMenu(this, point)) Close(ToolStripDropDownCloseReason.AppClicked);
        wasPressed = pressed;
    }
    private static bool ContainsMenu(ToolStripDropDown menu, Point point)
    {
        if (menu.Visible && menu.Bounds.Contains(point)) return true;
        return menu.Items.OfType<ToolStripDropDownItem>().Any(item => item.HasDropDownItems && item.DropDown.Visible && ContainsMenu(item.DropDown, point));
    }
    protected override void OnClosed(ToolStripDropDownClosedEventArgs e) { outsideClicks.Stop(); base.OnClosed(e); }
    protected override void Dispose(bool disposing) { if (disposing) outsideClicks.Dispose(); base.Dispose(disposing); }
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
    internal TraySubMenu() => AutoClose = false;
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
