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
            Reading g;
            try { g = GrokUsage.ReadLive(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception e) { g = new("Grok", [], null, e.Message); }
            Directory.CreateDirectory(Preferences.Folder);
            File.WriteAllText(Path.Combine(Preferences.Folder, "diagnostics.json"), JsonSerializer.Serialize(new { codex = c, antigravity = a, claudeCode = ClaudeCodeUsage.ReadSnapshot(), grok = g }, new JsonSerializerOptions { WriteIndented = true }));
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
    private readonly System.Windows.Forms.Timer hoverClose = new() { Interval = 30 };

    private readonly CancellationTokenSource stop = new();
    private readonly DetailsForm form;
    private readonly TrayWidget widget;
    private Reading codex = new("Codex", [], null), agy = new("Antigravity", [], null);
    private Reading? claude;
    private Reading grok = new("Grok", [], null);
    private bool busy, closing, hoverOpened;

    internal TrayApplicationContext()
    {
        form = new(prefs);
        prefs.Pinned = false;
        widget = new(new[] { tray, agyTray }.Concat(extraSlots).ToArray(), menu);
        widget.OpenDetails += () => { hoverOpened = false; if (form.Visible) form.HideImmediately(); else form.Reveal(true); };
        widget.HoverDetailsEnabled = () => prefs.OpenDetailsOnHover;
        widget.UpdateTooltip();
        widget.HoverDetails += () => { if (!form.Visible) { hoverOpened = true; form.Reveal(true, fromHover: true); } };
        form.VisibleChanged += (_, _) => { if (!form.Visible) hoverOpened = false; };
        menu.Opened += (_, _) => { if (hoverOpened) form.HideImmediately(); };
        hoverClose.Tick += (_, _) =>
        {
            if (!hoverOpened || !form.Visible || prefs.Pinned) return;
            if (widget.IsHovered || form.Bounds.Contains(Cursor.Position))
            {
                if (form.IsDismissing) form.Reveal(true, fromHover: true);
                return;
            }
            form.Dismiss();
        };
        form.TrayBounds = () => widget.Visible ? widget.ScreenBounds : TrayPresentation.GetBounds(selectedTray ?? tray);
        menu.TrayAnchor = () => widget.Visible ? widget.ScreenBounds : TrayPresentation.GetBounds(selectedTray ?? tray) ?? new Rectangle(Cursor.Position, Size.Empty);
        form.RefreshRequested += () => _ = Refresh();
        menu.Items.Add(L.T("パネルを開く"), null, (_, _) => { menu.CloseForAction(); hoverOpened = false; form.Reveal(true); });
        menu.Items.Add(L.T("今すぐ更新"), null, (_, _) => { menu.CloseForAction(); _ = Refresh(); });
        var hoverOption = new ToolStripMenuItem(L.T("ホバーで詳細を開く")) { CheckOnClick = true, Checked = prefs.OpenDetailsOnHover };
        hoverOption.CheckedChanged += (_, _) => { prefs.OpenDetailsOnHover = hoverOption.Checked; prefs.Save(); widget.UpdateTooltip(); };
        menu.Items.Add(hoverOption);
        var displayMenu = new ToolStripMenuItem(L.T("タスクバー表示")) { DropDown = new TraySubMenu() };
        var displayChoices = new List<(bool Top, string Provider, ToolStripMenuItem Item)>();
        foreach (var row in new[] { (Top: true, Label: L.T("上段")), (Top: false, Label: L.T("下段")) })
        {
            var rowMenu = new ToolStripMenuItem(row.Label) { DropDown = new TraySubMenu() };
            foreach (var provider in new[] { "Codex", "Antigravity", "Claude Code", "Grok" })
            {
                var item = new ToolStripMenuItem(provider);
                bool top = row.Top;
                item.Click += (_, _) =>
                {
                    prefs.SelectTaskbarProvider(top, provider);
                    UpdateDisplayChoices();
                    Apply();
                };
                displayChoices.Add((top, provider, item));
                rowMenu.DropDownItems.Add(item);
            }
            displayMenu.DropDownItems.Add(rowMenu);
        }
        void UpdateDisplayChoices()
        {
            foreach (var choice in displayChoices)
                choice.Item.Checked = (choice.Top ? prefs.TaskbarTop : prefs.TaskbarBottom) == choice.Provider;
        }
        UpdateDisplayChoices();
        menu.Items.Add(displayMenu);
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
        menu.Items.Add(L.T("終了"), null, (_, _) => { menu.CloseForAction(); ExitThread(); });
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
            icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) { selectedTray = icon; hoverOpened = false; if (form.Visible && !prefs.Pinned) form.HideImmediately(); else form.Reveal(true); } };
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
        var grokTask = RefreshGrok();
        claude = ClaudeCodeUsage.ReadSnapshot();
        try { codex = await Providers.Codex(stop.Token); }
        catch (OperationCanceledException) { codex = codex with { Error = L.T("接続がタイムアウトしました") }; }
        catch (Exception e) { codex = codex with { Error = e.Message }; }
        finally { await Task.WhenAll(agyTask, grokTask); busy = false; if (!closing) Apply(); }
    }
    private async Task RefreshAntigravity()
    {
        try { agy = await Providers.AntigravityLive(stop.Token); }
        catch (OperationCanceledException) { agy = agy with { Error = L.T("取得がタイムアウトしました") }; }
        catch (Exception e) { agy = agy with { Error = e.Message }; }
    }
    private async Task RefreshGrok()
    {
        try { grok = await GrokUsage.ReadLive(stop.Token); }
        catch (OperationCanceledException) { grok = grok with { Error = L.T("取得がタイムアウトしました") }; }
        catch (Exception e) { grok = grok with { Error = e.Message }; }
    }
    private void Apply()
    {
        if (closing) return;
        Reading? displayedClaude = claude;
        if (displayedClaude == null && (prefs.TaskbarTop == "Claude Code" || prefs.TaskbarBottom == "Claude Code"))
            displayedClaude = new Reading("Claude Code", [], null, L.T("Claude Codeの残量は利用できません"));
        Reading? displayedGrok = grok.Quotas.Count > 0 || prefs.TaskbarTop == "Grok" || prefs.TaskbarBottom == "Grok" ? grok : null;
        form.UpdateReadings(codex, agy, displayedClaude, displayedGrok, busy);
        var top = ForTaskbar(prefs.TaskbarTop, displayedClaude);
        var bottom = ForTaskbar(prefs.TaskbarBottom, displayedClaude);
        widget.UpdateReadings(top, bottom);
        form.PositionAtTray();
        tray.Text = TooltipText.For(top);
        agyTray.Text = TooltipText.For(bottom);
    }
    private Reading ForTaskbar(string provider, Reading? displayedClaude) => provider switch
    {
        "Antigravity" => agy,
        "Claude Code" => displayedClaude ?? new Reading("Claude Code", [], null, L.T("Claude Codeの残量は利用できません")),
        "Grok" => grok,
        _ => codex
    };
    protected override void ExitThreadCore()
    {
        closing = true; stop.Cancel(); poll.Dispose(); hoverClose.Dispose();
        tray.Visible = false; agyTray.Visible = false;
        widget.Dispose(); tray.Icon?.Dispose(); tray.Dispose(); agyTray.Icon?.Dispose(); agyTray.Dispose(); menu.Dispose(); form.Dispose();
        foreach (var slot in extraSlots) { slot.Visible = false; slot.Icon?.Dispose(); slot.Dispose(); }
        base.ExitThreadCore();
    }
}
