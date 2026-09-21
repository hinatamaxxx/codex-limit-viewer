namespace CodexLimitViewer;

internal sealed class TrayContextMenu : ContextMenuStrip
{
    internal TrayContextMenu() { AutoClose = true; }
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
