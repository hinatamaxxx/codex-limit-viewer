using System.Reflection;
using System.Runtime.InteropServices;


namespace UsageLimitViewer;

internal static class TrayPresentation
{
    [StructLayout(LayoutKind.Sequential)] private struct Identifier
    {
        public uint Size; public IntPtr Window; public uint Id; public Guid Guid;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref Identifier id, out Rect rect);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);

    // NotifyIcon does not expose its native identity, so read it from its private fields.
    // .NET Framework names these "window" and "id" (an int); newer .NET uses "_window" and "_id" (a uint).
    private static (IntPtr Window, uint Id)? Identity(NotifyIcon icon)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var windowField = typeof(NotifyIcon).GetField("window", flags) ?? typeof(NotifyIcon).GetField("_window", flags);
        var idField = typeof(NotifyIcon).GetField("id", flags) ?? typeof(NotifyIcon).GetField("_id", flags);
        if (windowField?.GetValue(icon) is not NativeWindow window || idField?.GetValue(icon) is not { } idValue) return null;
        return (window.Handle, Convert.ToUInt32(idValue));
    }

    internal static Rectangle? GetBounds(NotifyIcon icon)
    {
        if (Identity(icon) is not { } identity) return null;
        var id = new Identifier { Size = (uint)Marshal.SizeOf<Identifier>(), Window = identity.Window, Id = identity.Id };
        return Shell_NotifyIconGetRect(ref id, out var r) == 0 && r.Right > r.Left && r.Bottom > r.Top
            ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotifyData
    {
        public int Size; public IntPtr Window; public uint Id, Flags, CallbackMessage; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyData data);
    private static IntPtr lastBalloonIcon;

    // A notification whose body shows the app's own icon, large, instead of Windows' info/warning marks.
    // The heading (app name and small icon) comes from the AppUserModelID the app and its Start menu shortcut share.
    internal static void Notify(NotifyIcon icon, string title, string text)
    {
        if (Identity(icon) is { } identity)
        {
            int size = Math.Max(32, SystemInformation.IconSize.Width * 2);
            IntPtr large = IntPtr.Zero;
            try
            {
                using var appIcon = new Icon(typeof(TrayPresentation).Assembly.GetManifestResourceStream("UsageLimitViewerSetup.App.ico")!, size, size);
                large = ((Icon)appIcon.Clone()).Handle;
            }
            catch (Exception e) when (e is ArgumentException or IOException) { }
            var data = new NotifyData
            {
                Size = Marshal.SizeOf<NotifyData>(), Window = identity.Window, Id = identity.Id, Flags = 0x10 /* NIF_INFO */,
                Tip = "", Info = text, InfoTitle = title, InfoFlags = large == IntPtr.Zero ? 1u : 0x4u | 0x20u, // NIIF_USER | NIIF_LARGE_ICON
                BalloonIcon = large,
            };
            if (Shell_NotifyIcon(1 /* NIM_MODIFY */, ref data))
            {
                if (lastBalloonIcon != IntPtr.Zero) DestroyIcon(lastBalloonIcon);
                lastBalloonIcon = large;
                return;
            }
            if (large != IntPtr.Zero) DestroyIcon(large);
        }
        icon.ShowBalloonTip(10000, title, text, ToolTipIcon.None);
    }
    internal static Point Above(Rectangle icon, Size panel, Rectangle workArea)
    {
        int x = icon.Left + icon.Width / 2 - panel.Width / 2;
        int y = icon.Top - panel.Height - 8;
        // Top taskbars open below the icon instead of off-screen.
        if (y < workArea.Top) y = icon.Bottom + 8;
        return new(UsageLimitViewer.Compat.Clamp(x, workArea.Left, Math.Max(workArea.Left, workArea.Right - panel.Width)),
            UsageLimitViewer.Compat.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - panel.Height)));
    }
    internal static Icon ReservationIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        // Space reservation only: no obsolete gauge can appear behind the text widget.
        var h = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); } finally { DestroyIcon(h); }
    }
}
