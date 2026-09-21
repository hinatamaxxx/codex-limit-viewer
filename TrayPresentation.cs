using System.Reflection;
using System.Runtime.InteropServices;


namespace CodexLimitViewer;

internal static class TrayPresentation
{
    [StructLayout(LayoutKind.Sequential)] private struct Identifier
    {
        public uint Size; public IntPtr Window; public uint Id; public Guid Guid;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref Identifier id, out Rect rect);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);

    internal static Rectangle? GetBounds(NotifyIcon icon)
    {
        // NotifyIcon does not expose its native identity. This adapter is verified against our bundled .NET runtime.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        if (typeof(NotifyIcon).GetField("_window", flags)?.GetValue(icon) is not NativeWindow window ||
            typeof(NotifyIcon).GetField("_id", flags)?.GetValue(icon) is not uint uid) return null;
        var id = new Identifier { Size = (uint)Marshal.SizeOf<Identifier>(), Window = window.Handle, Id = uid };
        return Shell_NotifyIconGetRect(ref id, out var r) == 0 && r.Right > r.Left && r.Bottom > r.Top
            ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : null;
    }
    internal static Point Above(Rectangle icon, Size panel, Rectangle workArea)
    {
        int x = icon.Left + icon.Width / 2 - panel.Width / 2;
        int y = icon.Top - panel.Height - 8;
        // Top taskbars open below the icon instead of off-screen.
        if (y < workArea.Top) y = icon.Bottom + 8;
        return new(Math.Clamp(x, workArea.Left, Math.Max(workArea.Left, workArea.Right - panel.Width)),
            Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - panel.Height)));
    }
    internal static Icon ReservationIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        // Space reservation only: no obsolete gauge can appear behind the text widget.
        var h = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); } finally { DestroyIcon(h); }
    }
}
