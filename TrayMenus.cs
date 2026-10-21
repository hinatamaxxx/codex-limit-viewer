using System.Runtime.InteropServices;

namespace UsageLimitViewer;

// Radio: one of several exclusive choices, shown with the standard dot instead of a check mark.
internal sealed record MenuEntry(string Text, Action? Click = null, bool Checked = false, bool Enabled = true, bool Default = false,
    IReadOnlyList<MenuEntry>? Children = null, bool Radio = false)
{
    internal static readonly MenuEntry Separator = new("-");
    internal bool IsSeparator => ReferenceEquals(this, Separator);
}

// The shell's own popup menu: Windows draws the margins, check marks, scaling and dark mode, like other tray apps.
internal sealed class TrayMenu : IDisposable
{
    private const uint MIIM_STATE = 1, MIIM_ID = 2, MIIM_SUBMENU = 4, MIIM_FTYPE = 0x100, MIIM_STRING = 0x40;
    private const uint MFT_SEPARATOR = 0x800, MFT_RADIOCHECK = 0x200, MFS_GRAYED = 3, MFS_CHECKED = 8, MFS_DEFAULT = 0x1000;
    private const uint TPM_RIGHTBUTTON = 2, TPM_BOTTOMALIGN = 0x20, TPM_VERTICAL = 0x40, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool InsertMenuItem(IntPtr menu, uint item, bool byPosition, ref ItemInfo info);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ItemInfo
    {
        public int Size; public uint Mask, Type, State, Id; public IntPtr SubMenu, CheckedBitmap, UncheckedBitmap, Data;
        public string? Text; public uint TextLength; public IntPtr Bitmap;
    }
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, ref TrackParams parameters);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr w, IntPtr l);
    [DllImport("uxtheme.dll", EntryPoint = "#133")] private static extern bool AllowDarkModeForWindow(IntPtr window, bool allow);
    [DllImport("uxtheme.dll", EntryPoint = "#135")] private static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] private static extern void FlushMenuThemes();
    [StructLayout(LayoutKind.Sequential)] private struct TrackParams { public int Size; public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern bool GetMenuItemRect(IntPtr window, IntPtr menu, uint item, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);

    // Receives WM_MENUSELECT, which says which item (and so which submenu) is being opened.
    private sealed class MenuOwner : NativeWindow
    {
        internal IntPtr SelectedMenu;
        internal uint SelectedIndex;
        protected override void WndProc(ref Message m)
        {
            const int WM_MENUSELECT = 0x011F, MF_POPUP = 0x10;
            if (m.Msg == WM_MENUSELECT && ((long)m.WParam >> 16 & MF_POPUP) != 0)
            {
                SelectedMenu = m.LParam;
                SelectedIndex = (uint)((long)m.WParam & 0xFFFF);
            }
            base.WndProc(ref m);
        }
    }

    private readonly MenuOwner owner = new();
    private readonly WinEventProc popupEvent;   // Kept in a field: a collected delegate would crash the hook.
    private int openPopups;
    internal Func<IReadOnlyList<MenuEntry>>? Build;
    internal event Action? Opening;
    internal bool IsOpen { get; private set; }

    internal TrayMenu()
    {
        popupEvent = OnPopupEvent;
        // A hidden tool window owns the menu so it can take the foreground and close on an outside click.
        owner.CreateHandle(new CreateParams { Caption = "Usage Limit Viewer Menu", Style = unchecked((int)0x80000000), ExStyle = 0x80 });
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
            openPopups = 0;
            // EVENT_SYSTEM_MENUPOPUPSTART..END for this process only, delivered through this thread's message loop.
            var hook = taskbarBelow ? SetWinEventHook(0x0006, 0x0007, IntPtr.Zero, popupEvent, (uint)Compat.ProcessId, 0, 0) : IntPtr.Zero;
            int command;
            try { command = TrackPopupMenuEx(handle, flags, point.X, taskbarBelow ? anchor.Top : anchor.Bottom, owner.Handle, ref parameters); }
            finally
            {
                IsOpen = false;
                openPopups = 0;
                if (hook != IntPtr.Zero) UnhookWinEvent(hook);
            }
            PostMessage(owner.Handle, 0, IntPtr.Zero, IntPtr.Zero); // WM_NULL, so the next click on the tray closes a reopened menu.
            if (actions.TryGetValue(command, out var action)) action();
        }
        finally { DestroyMenu(handle); }
    }

    // Submenus open upward from their parent row, like the menu itself opens up from the taskbar. Windows lines a
    // submenu up with the top of its row and grows it downward; move it so its bottom meets the row's bottom instead.
    private void OnPopupEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (eventType == 0x0007) { if (openPopups > 0) openPopups--; return; }
        if (++openPopups < 2 || owner.SelectedMenu == IntPtr.Zero) return; // The first popup is the menu itself.
        if (!GetMenuItemRect(IntPtr.Zero, owner.SelectedMenu, owner.SelectedIndex, out var row) || !GetWindowRect(window, out var popup)) return;
        if (popup.Bottom <= row.Bottom) return; // Windows already opened it upward.
        var area = Screen.FromPoint(new Point(row.Left, row.Top)).WorkingArea;
        int top = Math.Max(area.Top, row.Bottom - (popup.Bottom - popup.Top));
        SetWindowPos(window, IntPtr.Zero, popup.Left, top, 0, 0, 0x0001 | 0x0004 | 0x0010); // NOSIZE | NOZORDER | NOACTIVATE
    }

    private static void Append(IntPtr menu, IReadOnlyList<MenuEntry> entries, Dictionary<int, Action> actions)
    {
        uint position = 0;
        foreach (var entry in entries)
        {
            var info = new ItemInfo { Size = Marshal.SizeOf<ItemInfo>(), Mask = MIIM_FTYPE };
            if (entry.IsSeparator) info.Type = MFT_SEPARATOR;
            else
            {
                info.Mask |= MIIM_STATE | MIIM_STRING;
                info.Type = entry.Radio ? MFT_RADIOCHECK : 0;
                info.State = (entry.Checked ? MFS_CHECKED : 0) | (entry.Enabled ? 0 : MFS_GRAYED) | (entry.Default ? MFS_DEFAULT : 0);
                info.Text = entry.Text.Replace("&", "&&");
                if (entry.Children is { } children)
                {
                    info.Mask |= MIIM_SUBMENU;
                    info.SubMenu = CreatePopupMenu();
                    Append(info.SubMenu, children, actions);
                }
                else
                {
                    info.Mask |= MIIM_ID;
                    info.Id = (uint)(actions.Count + 1);
                    actions[actions.Count + 1] = entry.Click ?? (() => { });
                }
            }
            InsertMenuItem(menu, position++, true, ref info);
        }
    }

    public void Dispose() => owner.DestroyHandle();
}
