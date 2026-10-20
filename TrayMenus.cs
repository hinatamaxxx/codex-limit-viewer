using System.Runtime.InteropServices;

namespace CodexLimitViewer;

internal sealed record MenuEntry(string Text, Action? Click = null, bool Checked = false, bool Enabled = true, bool Default = false,
    IReadOnlyList<MenuEntry>? Children = null)
{
    internal static readonly MenuEntry Separator = new("-");
    internal bool IsSeparator => ReferenceEquals(this, Separator);
}

// The shell's own popup menu: Windows draws the margins, check marks, scaling and dark mode, like other tray apps.
internal sealed class TrayMenu : IDisposable
{
    private const uint MF_STRING = 0, MF_GRAYED = 1, MF_CHECKED = 8, MF_POPUP = 0x10, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 2, TPM_BOTTOMALIGN = 0x20, TPM_VERTICAL = 0x40, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, IntPtr id, string? text);
    [DllImport("user32.dll")] private static extern bool SetMenuDefaultItem(IntPtr menu, uint id, uint byPosition);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, ref TrackParams parameters);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr w, IntPtr l);
    [DllImport("uxtheme.dll", EntryPoint = "#133")] private static extern bool AllowDarkModeForWindow(IntPtr window, bool allow);
    [DllImport("uxtheme.dll", EntryPoint = "#135")] private static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] private static extern void FlushMenuThemes();
    [StructLayout(LayoutKind.Sequential)] private struct TrackParams { public int Size; public int Left, Top, Right, Bottom; }

    private readonly NativeWindow owner = new();
    internal Func<IReadOnlyList<MenuEntry>>? Build;
    internal event Action? Opening;
    internal bool IsOpen { get; private set; }

    internal TrayMenu()
    {
        // A hidden tool window owns the menu so it can take the foreground and close on an outside click.
        owner.CreateHandle(new CreateParams { Caption = "Codex Limit Viewer Menu", Style = unchecked((int)0x80000000), ExStyle = 0x80 });
        // Explorer's undocumented switch for dark Win32 menus. Without it (older Windows) the menu stays light.
        try { SetPreferredAppMode(1); AllowDarkModeForWindow(owner.Handle, true); FlushMenuThemes(); }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException) { }
    }

    // anchor: the icon or taskbar text the menu belongs to; the menu opens beside it without covering it.
    internal void Show(Rectangle anchor, Point? at = null)
    {
        if (IsOpen || Build == null) return;
        Opening?.Invoke();
        var actions = new Dictionary<int, Action>();
        var handle = CreatePopupMenu();
        try
        {
            Append(handle, Build(), actions);
            try { FlushMenuThemes(); } catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException) { }
            var point = at ?? Cursor.Position;
            var area = Screen.FromPoint(point).WorkingArea;
            bool taskbarBelow = anchor.Top >= area.Bottom - 1;
            if (anchor.IsEmpty) anchor = new Rectangle(point, new Size(1, 1));
            var parameters = new TrackParams { Size = Marshal.SizeOf<TrackParams>(), Left = anchor.Left, Top = anchor.Top, Right = anchor.Right, Bottom = anchor.Bottom };
            uint flags = TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON | TPM_VERTICAL | (taskbarBelow ? TPM_BOTTOMALIGN : 0);
            SetForegroundWindow(owner.Handle);
            IsOpen = true;
            int command;
            try { command = TrackPopupMenuEx(handle, flags, point.X, taskbarBelow ? anchor.Top : anchor.Bottom, owner.Handle, ref parameters); }
            finally { IsOpen = false; }
            PostMessage(owner.Handle, 0, IntPtr.Zero, IntPtr.Zero); // WM_NULL, so the next click on the tray closes a reopened menu.
            if (actions.TryGetValue(command, out var action)) action();
        }
        finally { DestroyMenu(handle); }
    }

    private static void Append(IntPtr menu, IReadOnlyList<MenuEntry> entries, Dictionary<int, Action> actions)
    {
        foreach (var entry in entries)
        {
            if (entry.IsSeparator) { AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null); continue; }
            uint flags = (entry.Checked ? MF_CHECKED : 0) | (entry.Enabled ? 0 : MF_GRAYED);
            string text = entry.Text.Replace("&", "&&");
            if (entry.Children is { } children)
            {
                var submenu = CreatePopupMenu();
                Append(submenu, children, actions);
                AppendMenu(menu, flags | MF_POPUP, submenu, text);
                continue;
            }
            int id = actions.Count + 1;
            actions[id] = entry.Click ?? (() => { });
            AppendMenu(menu, flags | MF_STRING, id, text);
            if (entry.Default) SetMenuDefaultItem(menu, (uint)id, 0);
        }
    }

    public void Dispose() => owner.DestroyHandle();
}
