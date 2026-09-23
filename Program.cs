using System.Text.Json;
using Microsoft.Win32;

namespace CodexLimitViewer;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return Verification.Run();
        if (args.Contains("--diagnose"))
        {
            Reading c;
            try { c = Providers.Codex(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception e) { c = new("Codex", [], null, e.Message); }
            Reading a;
            try { a = Providers.AntigravityLive(CancellationToken.None).GetAwaiter().GetResult(); } catch { a = new("Antigravity", [], null, L.T("受信ファイルを読み取れません")); }
            Directory.CreateDirectory(Preferences.Folder);
            File.WriteAllText(Path.Combine(Preferences.Folder, "diagnostics.json"), JsonSerializer.Serialize(new { codex = c, antigravity = a }, new JsonSerializerOptions { WriteIndented = true }));
            return c.Quotas.Count > 0 ? 0 : 1;
        }
        L.English = args.Contains("--english") || (!args.Contains("--japanese") && Preferences.Load().Language == "en");
        ApplicationConfiguration.Initialize();
        if (args.Contains("--render-preview")) return Verification.Render();
        if (args.Contains("--render-live")) return Verification.Render(true);
        using var mutex = new Mutex(true, "Local\\CodexLimitViewer", out bool created);
        if (!created) return 0;
        Application.Run(new TrayApplicationContext());
        return 0;
    }
}
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Preferences prefs = Preferences.Load();
    private readonly NotifyIcon tray = new();
    private readonly NotifyIcon agyTray = new();
    private readonly NotifyIcon[] extraSlots = Enumerable.Range(0, 2).Select(_ => new NotifyIcon()).ToArray();
    private NotifyIcon? selectedTray;
    private readonly TrayContextMenu menu = new();
    private readonly System.Windows.Forms.Timer poll = new() { Interval = 60000 };
    private readonly System.Windows.Forms.Timer hoverClose = new() { Interval = 100 };

    private readonly CancellationTokenSource stop = new();
    private readonly DetailsForm form;
    private readonly TrayWidget widget;
    private Reading codex = new("Codex", [], null), agy = new("Antigravity", [], null);
    private bool busy, closing, hoverOpened;
    private long outsideSince;

    internal TrayApplicationContext()
    {
        form = new(prefs);
        prefs.Pinned = false;
        widget = new(new[] { tray, agyTray }.Concat(extraSlots).ToArray(), menu);
        widget.OpenDetails += () => { hoverOpened = false; if (form.Visible) form.Hide(); else form.Reveal(true); };
        widget.HoverDetailsEnabled = () => prefs.OpenDetailsOnHover;
        widget.HoverDetails += () => { if (!form.Visible) { hoverOpened = true; outsideSince = 0; form.Reveal(true); } };
        form.VisibleChanged += (_, _) => { if (!form.Visible) { hoverOpened = false; outsideSince = 0; } };
        menu.Opened += (_, _) => { if (hoverOpened) form.Hide(); };
        hoverClose.Tick += (_, _) =>
        {
            if (!hoverOpened || !form.Visible || prefs.Pinned) return;
            if (widget.IsHovered || form.Bounds.Contains(Cursor.Position)) { outsideSince = 0; return; }
            if (outsideSince == 0) outsideSince = Environment.TickCount64;
            else if (Environment.TickCount64 - outsideSince >= 350) form.Hide();
        };
        form.TrayBounds = () => widget.Visible ? widget.ScreenBounds : TrayPresentation.GetBounds(selectedTray ?? tray);
        menu.TrayAnchor = () => widget.Visible ? widget.ScreenBounds : TrayPresentation.GetBounds(selectedTray ?? tray) ?? new Rectangle(Cursor.Position, Size.Empty);
        form.RefreshRequested += () => _ = Refresh();
        menu.Items.Add(L.T("パネルを開く"), null, (_, _) => { hoverOpened = false; form.Reveal(true); });
        menu.Items.Add(L.T("今すぐ更新"), null, (_, _) => _ = Refresh());
        var hoverOption = new ToolStripMenuItem(L.T("ホバーで詳細を開く")) { CheckOnClick = true, Checked = prefs.OpenDetailsOnHover };
        hoverOption.CheckedChanged += (_, _) => { prefs.OpenDetailsOnHover = hoverOption.Checked; prefs.Save(); };
        menu.Items.Add(hoverOption);
        var startup = new ToolStripMenuItem(L.T("Windows起動時に開始")) { CheckOnClick = true };
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) startup.Checked = key?.GetValue("CodexLimitViewer") != null;
        startup.Click += (_, _) =>
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (startup.Checked) key.SetValue("CodexLimitViewer", "\"" + Environment.ProcessPath + "\""); else key.DeleteValue("CodexLimitViewer", false);
            }
            catch { startup.Checked = !startup.Checked; MessageBox.Show(L.T("自動起動の設定を保存できませんでした。"), "Codex Limit Viewer"); }
        };
        menu.Items.Add(startup);
        var languageMenu = new ToolStripMenuItem("Language / 言語") { DropDown = new TraySubMenu() };
        foreach (var language in new[] { ("日本語", "ja"), ("English", "en") })
        {
            var item = new ToolStripMenuItem(language.Item1) { Checked = prefs.Language == language.Item2 };
            item.Click += (_, _) =>
            {
                prefs.Language = language.Item2; prefs.Save();
                Application.Restart();
            };
            languageMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(languageMenu);
        menu.Items.Add(L.T("終了"), null, (_, _) => ExitThread());
        tray.Icon = TrayPresentation.ReservationIcon(); tray.Text = "Codex Limit Viewer"; tray.ContextMenuStrip = menu; tray.Visible = true;
        agyTray.Icon = TrayPresentation.ReservationIcon(); agyTray.Text = "Antigravity"; agyTray.ContextMenuStrip = menu; agyTray.Visible = true;
        int slotNumber = 0;
        foreach (var slot in extraSlots)
        {
            slot.Icon = (Icon)tray.Icon.Clone(); slot.Text = $"Codex Limit Viewer 表示スペース {++slotNumber}";
            slot.ContextMenuStrip = menu; slot.Visible = true;
        }
        form.PositionAtTray();
        foreach (var icon in new[] { tray, agyTray })
        {
            icon.MouseDown += (_, _) => selectedTray = icon;
            icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) { selectedTray = icon; hoverOpened = false; if (form.Visible && !prefs.Pinned) form.Hide(); else form.Reveal(true); } };
        }
        poll.Tick += (_, _) => _ = Refresh();

        poll.Start();
        hoverClose.Start();
        if (prefs.Pinned) { form.PositionAtTray(); form.Show(); }
        _ = Refresh();
    }
    private async Task Refresh()
    {
        if (busy || closing) return;
        busy = true; Apply();
        var agyTask = RefreshAntigravity();
        try { codex = await Providers.Codex(stop.Token); }
        catch (OperationCanceledException) { codex = codex with { Error = L.T("接続がタイムアウトしました") }; }
        catch (Exception e) { codex = codex with { Error = e.Message }; }
        finally { await agyTask; busy = false; if (!closing) Apply(); }
    }
    private async Task RefreshAntigravity()
    {
        try { agy = await Providers.AntigravityLive(stop.Token); }
        catch (OperationCanceledException) { agy = agy with { Error = L.T("取得がタイムアウトしました") }; }
        catch (Exception e) { agy = agy with { Error = e.Message }; }
    }
    private void Apply()
    {
        if (closing) return;
        form.UpdateReadings(codex, agy, busy);
        widget.UpdateReadings(codex, agy);
        form.PositionAtTray();
        tray.Text = TooltipText.For(codex);
        agyTray.Text = TooltipText.For(agy);
    }
    protected override void ExitThreadCore()
    {
        closing = true; stop.Cancel(); poll.Dispose(); hoverClose.Dispose();
        tray.Visible = false; agyTray.Visible = false;
        widget.Dispose(); tray.Icon?.Dispose(); tray.Dispose(); agyTray.Icon?.Dispose(); agyTray.Dispose(); menu.Dispose(); form.Dispose();
        foreach (var slot in extraSlots) { slot.Visible = false; slot.Icon?.Dispose(); slot.Dispose(); }
        base.ExitThreadCore();
    }
}
