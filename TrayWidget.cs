using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexLimitViewer;

// Notification slots reserve space; this single surface paints across only those slots.
internal sealed class TrayWidget : Form
{
    private readonly NotifyIcon[] slots;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly ToolTip tooltip = new();
    private Reading codex = new("Codex", [], null), agy = new("Antigravity", [], null);
    private bool needsPaint = true;
    private bool hovered;
    internal event Action? OpenDetails;
    internal TrayWidget(NotifyIcon[] slots, ContextMenuStrip menu)
    {
        this.slots = slots;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
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
        if (message.Msg == 0x0202) { OpenDetails?.Invoke(); KeepAboveTaskbar(); }
    }
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80 | 0x80000; return p; }
    }
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRect rect, int size);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    private static bool ForegroundCovers(Rectangle area)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        GetWindowThreadProcessId(foreground, out var process);
        if (process == Environment.ProcessId) return false;
        if (DwmGetWindowAttribute(foreground, 9, out var rect, Marshal.SizeOf<NativeRect>()) != 0 &&
            !GetWindowRect(foreground, out rect)) return false;
        return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom).IntersectsWith(area);
    }
    private void KeepAboveTaskbar()
    {
        if (Visible) SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0013); // no move, resize or activation
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); BeginInvoke(() => ShowWindow(Handle, 4)); }
    internal void UpdateReadings(Reading c, Reading a)
    {
        codex = c; agy = a;
        needsPaint = true;
        tooltip.SetToolTip(this, TooltipText.For(c) + "\n" + TooltipText.For(a));
        Align(); Invalidate();
    }
    private void Align()
    {
        var rectangles = slots.Select(TrayPresentation.GetBounds).ToArray();
        if (rectangles.Any(r => r == null)) { Hide(); return; }
        var ordered = rectangles.Select(r => r!.Value).OrderBy(r => r.Left).ToArray();
        var area = ordered.Aggregate(Rectangle.Union);
        var screen = Screen.FromRectangle(area);
        bool adjacent = ordered.All(r => Math.Abs(r.Top - area.Top) <= 2) &&
            area.Width <= ordered.Sum(r => r.Width) + 2 &&
            ordered.Zip(ordered.Skip(1)).All(pair => Math.Abs(pair.First.Right - pair.Second.Left) <= 2);
        bool onTaskbar = !screen.WorkingArea.Contains(area) && screen.Bounds.IntersectsWith(area);
        if (!adjacent || !onTaskbar || ForegroundCovers(area)) { Hide(); return; }
        int height = area.Height;
        var bounds = new Rectangle(area.Left, area.Top + (area.Height - height) / 2, area.Width, height);
        if (Bounds != bounds) { Bounds = bounds; needsPaint = true; }
        if (!Visible) { Show(); needsPaint = true; }
        bool pointerInside = Bounds.Contains(Cursor.Position);
        if (hovered != pointerInside) { hovered = pointerInside; needsPaint = true; }
        if (needsPaint) { RenderSurface(); needsPaint = false; }
        KeepAboveTaskbar();
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }
    private void RenderSurface()
    {
        using var bitmap = ClockTextRenderer.Render(Width, Height, DeviceDpi, codex, agy, hovered);
        LayeredSurface.Present(Handle, bitmap, Location);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); tooltip.Dispose(); }
        base.Dispose(disposing);
    }
}
