using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexLimitViewer;

// Notification slots reserve space; this single surface paints across only those slots.
internal sealed class TrayWidget : Form
{
    private readonly NotifyIcon[] slots;
    private readonly ContextMenuStrip menu;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly ToolTip tooltip = new();
    private Reading topReading = new("Codex", [], null), bottomReading = new("Antigravity", [], null);
    private bool needsPaint = true;
    private bool hovered;
    private bool hoverHandled;
    private bool tooltipVisible;
    private long hoverStarted;
    private Point taskbarPosition;
    internal bool IsHovered => hovered;
    internal event Action? OpenDetails;
    internal event Action? HoverDetails;
    internal Func<bool>? HoverDetailsEnabled;
    internal TrayWidget(NotifyIcon[] slots, ContextMenuStrip menu)
    {
        this.slots = slots;
        this.menu = menu;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = false;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(35, 35, 35);
        Text = "Codex Limit Viewer Taskbar";
        AccessibleName = L.T("CodexとAntigravityの残り使用量");
        ContextMenuStrip = menu;
        Cursor = Cursors.Hand;
        timer.Tick += (_, _) => Align();
        timer.Start();
    }
    protected override bool ShowWithoutActivation => true;
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0021) // WM_MOUSEACTIVATE: don't activate the taskbar underneath.
        {
            message.Result = (IntPtr)3; // MA_NOACTIVATE; still deliver the click.
            return;
        }
        base.WndProc(ref message);
        if (message.Msg == 0x0202) OpenDetails?.Invoke();
    }
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80 | 0x80000; return p; }
    }
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    private IntPtr taskbar;
    internal Rectangle ScreenBounds => RectangleToScreen(ClientRectangle);
    private bool AttachToTaskbar(Rectangle area)
    {
        if (taskbar != IntPtr.Zero && IsWindow(taskbar)) return true;
        taskbar = IntPtr.Zero;
        var parent = FindWindow("Shell_TrayWnd", null);
        if (parent == IntPtr.Zero || !GetWindowRect(parent, out var r) ||
            !Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom).Contains(area)) return false;
        var style = GetWindowLongPtr(Handle, -16).ToInt64();
        SetWindowLongPtr(Handle, -16, (IntPtr)((style & ~0x80000000L) | 0x40000000L));
        if (SetParent(Handle, parent) == IntPtr.Zero)
        {
            SetWindowLongPtr(Handle, -16, (IntPtr)style);
            return false;
        }
        taskbar = parent;
        return true;
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); BeginInvoke(() => ShowWindow(Handle, 4)); }
    internal void UpdateReadings(Reading top, Reading bottom)
    {
        topReading = top; bottomReading = bottom;
        needsPaint = true;
        Align(); Invalidate();
    }
    internal void UpdateTooltip()
    {
        if (!tooltipVisible) return;
        tooltip.Hide(this);
        tooltipVisible = false;
    }
    private void Align()
    {
        var rectangles = slots.Select(TrayPresentation.GetBounds).ToArray();
        if (rectangles.Any(r => r == null)) { UpdateTooltip(); Hide(); return; }
        var ordered = rectangles.Select(r => r!.Value).OrderBy(r => r.Left).ToArray();
        var area = ordered.Aggregate(Rectangle.Union);
        var screen = Screen.FromRectangle(area);
        bool adjacent = ordered.All(r => Math.Abs(r.Top - area.Top) <= 2) &&
            area.Width <= ordered.Sum(r => r.Width) + 2 &&
            ordered.Zip(ordered.Skip(1)).All(pair => Math.Abs(pair.First.Right - pair.Second.Left) <= 2);
        bool onTaskbar = !screen.WorkingArea.Contains(area) && screen.Bounds.IntersectsWith(area);
        if (!adjacent || !onTaskbar || !AttachToTaskbar(area)) { UpdateTooltip(); Hide(); return; }
        int height = area.Height;
        GetWindowRect(taskbar, out var parentBounds);
        var bounds = new Rectangle(area.Left - parentBounds.Left, area.Top - parentBounds.Top,
            area.Width, height);
        taskbarPosition = bounds.Location;
        if (Bounds != bounds) { Bounds = bounds; needsPaint = true; }
        if (!Visible) { Show(); needsPaint = true; }
        bool pointerInside = ScreenBounds.Contains(Cursor.Position) && WindowFromPoint(Cursor.Position) == Handle;
        if (hovered != pointerInside)
        {
            hovered = pointerInside;
            hoverHandled = false;
            hoverStarted = Environment.TickCount64;
            needsPaint = true;
        }
        if (hovered && !hoverHandled)
        {
            if (menu.Visible) hoverHandled = true;
            else if (Environment.TickCount64 - hoverStarted >= 400 && HoverDetailsEnabled?.Invoke() == true)
            {
                hoverHandled = true;
                HoverDetails?.Invoke();
            }
        }
        bool showName = hovered && !menu.Visible && HoverDetailsEnabled?.Invoke() == false &&
            Environment.TickCount64 - hoverStarted >= SystemInformation.MouseHoverTime;
        if (showName && !tooltipVisible)
        {
            const string appName = "Codex Limit Viewer";
            int tooltipWidth = TextRenderer.MeasureText(appName, SystemFonts.StatusFont).Width + 8;
            tooltip.Show(appName, this, (Width - tooltipWidth) / 2, -8, 5000);
            tooltipVisible = true;
        }
        else if (!showName) UpdateTooltip();
        if (needsPaint) { RenderSurface(); needsPaint = false; }
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }
    private void RenderSurface()
    {
        using var bitmap = ClockTextRenderer.Render(Width, Height, DeviceDpi, topReading, bottomReading, hovered);
        LayeredSurface.Present(Handle, bitmap, taskbarPosition);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); tooltip.Dispose(); }
        base.Dispose(disposing);
    }
}
