using System.Net.Http;
using System.Text.Json;

namespace CodexLimitViewer;

internal static class Program
{
    // Matches the Start menu shortcut, so Windows shows that name and icon for this app's windows and notifications.
    internal const string AppUserModelId = "hinatamaxxx.CodexLimitViewer";
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [STAThread]
    static int Main(string[] args)
    {
        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        if (args.Contains("--self-test")) return Verification.Run();
        if (args.Contains("--diagnose"))
        {
            ClaudeCodeUsage.EnsureBridge();
            Reading c;
            try { c = Providers.Codex(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception e) { c = new("Codex", [], null, e.Message); }
            Reading a;
            try { a = Providers.AntigravityLive(CancellationToken.None).GetAwaiter().GetResult(); } catch { a = new("Antigravity", [], null, L.T("受信ファイルを読み取れません")); }
            Reading g;
            try { g = GrokUsage.ReadLive(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception e) { g = new("Grok", [], null, e.Message); }
            Reading cl;
            try { cl = ClaudeUsageApi.ReadLive(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception e) { cl = new("Claude Code", [], null, e.Message); }
            Directory.CreateDirectory(Preferences.Folder);
            File.WriteAllText(Path.Combine(Preferences.Folder, "diagnostics.json"), JsonSerializer.Serialize(new { codex = c, antigravity = a, claudeCode = cl, claudeCodeStatusLine = ClaudeCodeUsage.ReadSnapshot(), grok = g }, new JsonSerializerOptions { WriteIndented = true }));
            return c.Quotas.Count > 0 ? 0 : 1;
        }
        L.English = args.Contains("--english") || (!args.Contains("--japanese") && Preferences.Load().Language == "en");
        ApplicationConfiguration.Initialize();
        if (args.Contains("--render-preview")) return Verification.Render();
        if (args.Contains("--render-live")) return Verification.Render(true);
        using var mutex = new Mutex(false, "Local\\CodexLimitViewer");
        bool owns;
        // After a language switch the previous copy is still closing; wait for it instead of quitting.
        try { owns = mutex.WaitOne(args.Contains("--restarted") ? 15000 : 0); }
        catch (AbandonedMutexException) { owns = true; }
        if (!owns)
        {
            // Starting the app again (for example from the Start menu) opens the running copy's panel.
            try { using var show = EventWaitHandle.OpenExisting(ShowSignal); show.Set(); }
            catch (Exception e) when (e is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException) { }
            return 0;
        }
        try
        {
            ClaudeCodeUsage.EnsureBridge();
            Application.Run(new TrayApplicationContext());
        }
        finally { mutex.ReleaseMutex(); }
        return 0;
    }
    internal const string ShowSignal = "Local\\CodexLimitViewer_Show";
}
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Preferences prefs = Preferences.Load();
    private readonly NotifyIcon tray = new();
    private readonly NotifyIcon agyTray = new();
    // Keep four slots: Windows 11 hides newly added tray icons in the overflow, which put a fifth slot under the ^ button.
    private readonly NotifyIcon[] extraSlots = Enumerable.Range(0, 2).Select(_ => new NotifyIcon()).ToArray();
    private NotifyIcon? selectedTray;
    private readonly TrayMenu menu = new();
    private readonly System.Windows.Forms.Timer poll = new() { Interval = 60000 };
    private readonly System.Windows.Forms.Timer hoverClose = new() { Interval = 30 };
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 6 * 60 * 60 * 1000 };
    private readonly EventWaitHandle showSignal = new(false, EventResetMode.AutoReset, Program.ShowSignal);
    private readonly RegisteredWaitHandle showWait;

    private readonly CancellationTokenSource stop = new();
    private readonly DetailsForm form;
    private readonly TrayWidget widget;
    private Reading codex = new("Codex", [], null), agy = new("Antigravity", [], null);
    private Reading? claude = ClaudeUsageApi.LoadCache();
    private Reading grok = new("Grok", [], null);
    private Reading shownTop = new("Codex", [], null), shownBottom = new("Antigravity", [], null);
    private Version? newerVersion;
    private bool busy, closing, hoverOpened, forceQueued, confirmingExit;

    internal TrayApplicationContext()
    {
        form = new(prefs);
        ClockTextRenderer.FiveHourFirst = prefs.FiveHourFirst;
        prefs.Pinned = false;
        widget = new(new[] { tray, agyTray }.Concat(extraSlots).ToArray(), menu);
        widget.OpenDetails += () => { hoverOpened = false; if (form.Visible) form.HideImmediately(); else form.Reveal(true); };
        widget.HoverDetailsEnabled = () => prefs.OpenDetailsOnHover;
        widget.TooltipText = () => TooltipText.For(TooltipText.Title,
            TooltipText.Status(shownTop) + " · " + TooltipText.Status(shownBottom), TooltipText.UpdateNotice(newerVersion));
        widget.UpdateTooltip();
        widget.HoverDetails += () => { if (!form.Visible) { hoverOpened = true; form.Reveal(true, fromHover: true); } };
        form.VisibleChanged += (_, _) => { if (!form.Visible) hoverOpened = false; };
        menu.Opening += () => { if (hoverOpened) form.HideImmediately(); };
        menu.Build = BuildMenu;
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
        form.RefreshRequested += () => _ = Refresh(force: true);
        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        showWait = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => ui.Post(_ => OpenPanel(), null), null, -1, false);
        tray.Icon = TrayPresentation.ReservationIcon(); tray.Text = "Codex Limit Viewer"; tray.Visible = true;
        agyTray.Icon = TrayPresentation.ReservationIcon(); agyTray.Text = "Antigravity"; agyTray.Visible = true;
        int slotNumber = 0;
        foreach (var slot in extraSlots)
        {
            slot.Icon = (Icon)tray.Icon.Clone(); slot.Text = $"Codex Limit Viewer 表示スペース {++slotNumber}";
            slot.Visible = true;
        }
        foreach (var icon in new[] { tray, agyTray }.Concat(extraSlots))
            icon.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) menu.Show(TrayPresentation.GetBounds(icon) ?? Rectangle.Empty); };
        form.PositionAtTray();
        foreach (var icon in new[] { tray, agyTray })
        {
            icon.MouseDown += (_, _) => selectedTray = icon;
            icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) { selectedTray = icon; hoverOpened = false; if (form.Visible && !prefs.Pinned) form.HideImmediately(); else form.Reveal(true); } };
            icon.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenPanel(); };
        }
        poll.Tick += (_, _) => _ = Refresh();
        updateTimer.Tick += (_, _) => _ = CheckForUpdate();

        poll.Start();
        hoverClose.Start();
        updateTimer.Start();
        if (prefs.Pinned) { form.PositionAtTray(); form.Show(); }
        _ = Refresh();
        _ = CheckForUpdate();
    }
    private void OpenPanel()
    {
        if (closing) return;
        hoverOpened = false;
        form.Reveal(true);
    }
    // Rebuilt on every open, so check marks always match the current settings and Windows' startup switch.
    private IReadOnlyList<MenuEntry> BuildMenu()
    {
        var entries = new List<MenuEntry> { new(TooltipText.Title, Enabled: false), new(StatusLine(), Enabled: false) };
        if (newerVersion is { } version)
            entries.Add(new(L.F("新しいバージョン {version} をダウンロード", ("version", version.ToString(3))), UpdateCheck.OpenReleasePage));
        entries.Add(MenuEntry.Separator);
        entries.Add(new(L.T("パネルを開く"), OpenPanel, Default: true));
        entries.Add(new(L.T("今すぐ更新"), () => _ = Refresh(force: true)));
        var providers = new[] { "Codex", "Antigravity", "Claude Code", "Grok" };
        MenuEntry Row(bool top, string label) => new(label, Children: providers.Select(provider => new MenuEntry(provider, () =>
        {
            prefs.SelectTaskbarProvider(top, provider);
            Apply();
        }, Checked: (top ? prefs.TaskbarTop : prefs.TaskbarBottom) == provider)).ToList());
        MenuEntry Order(bool fiveHourFirst, string label) => new(label, () =>
        {
            prefs.FiveHourFirst = fiveHourFirst; prefs.Save();
            ClockTextRenderer.FiveHourFirst = fiveHourFirst;
            Apply();
        }, Checked: prefs.FiveHourFirst == fiveHourFirst);
        entries.Add(new(L.T("タスクバー表示"), Children: [Row(true, L.T("上段")), Row(false, L.T("下段")), MenuEntry.Separator,
            Order(true, L.T("5時間（週間）の順")), Order(false, L.T("週間（5時間）の順"))]));
        entries.Add(new(L.T("その他"), Children:
        [
            new(L.T("ホバーで詳細を開く"), () => { prefs.OpenDetailsOnHover = !prefs.OpenDetailsOnHover; prefs.Save(); widget.UpdateTooltip(); }, Checked: prefs.OpenDetailsOnHover),
            new(L.T("Windows起動時に自動起動"), ToggleStartup, Checked: StartupShortcut.IsEnabled),
            new("Language / 言語", Children: new[] { ("日本語", "ja"), ("English", "en") }.Select(language =>
                new MenuEntry(language.Item1, () => SwitchLanguage(language.Item2), Checked: prefs.Language == language.Item2)).ToList()),
        ]));
        entries.Add(MenuEntry.Separator);
        entries.Add(new(L.T("終了"), ConfirmExit));
        return entries;
    }
    private string StatusLine()
    {
        if (busy) return L.T("残量を更新しています…");
        var latest = new Reading?[] { codex, agy, claude, grok }.Where(r => r is { Updated: not null, Error: null }).Select(r => r!.Updated!.Value).DefaultIfEmpty().Max();
        return latest == default ? L.T("残量を取得できていません")
            : L.F("{time} に残量を更新しました", ("time", latest.ToLocalTime().ToString("H:mm")));
    }
    private void ToggleStartup()
    {
        try
        {
            if (StartupShortcut.IsEnabled) StartupShortcut.SetEnabled(false);
            else if (!StartupShortcut.SetEnabled(true))
            {
                // Windows keeps its own on/off switch for the shortcut; the app never overrides the user's choice there.
                MessageBox.Show(L.T("Windowsのスタートアップ設定でオフになっています。開いた設定画面で「Codex Limit Viewer」をオンにしてください。"),
                    "Codex Limit Viewer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                StartupShortcut.OpenWindowsSettings();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(L.T("自動起動の設定を保存できませんでした。"), "Codex Limit Viewer");
        }
    }
    private void SwitchLanguage(string language)
    {
        if (prefs.Language == language) return;
        prefs.Language = language; prefs.Save();
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--restarted") { UseShellExecute = false })?.Dispose(); }
        catch (Exception) { return; }
        ExitThread();
    }
    private void ConfirmExit()
    {
        if (confirmingExit) return;
        confirmingExit = true;
        try
        {
            using var dialog = new ExitDialog();
            if (dialog.ShowDialog() == DialogResult.OK) ExitThread();
        }
        finally { confirmingExit = false; }
    }
    private async Task CheckForUpdate()
    {
        try { newerVersion = await UpdateCheck.FindNewer(stop.Token); }
        catch (Exception) { } // Offline or rate-limited: try again at the next check.
        if (!closing) Apply();
    }
    // force: a manual refresh, which asks Claude right away instead of waiting out the polling interval.
    private async Task Refresh(bool force = false)
    {
        // A manual refresh during a running one is replayed right after it, not dropped.
        if (busy) { forceQueued |= force; return; }
        if (closing) return;
        busy = true; Apply();
        var agyTask = RefreshAntigravity();
        var grokTask = RefreshGrok();
        var claudeTask = RefreshClaude(force);
        try { codex = await Providers.Codex(stop.Token); }
        catch (OperationCanceledException) { codex = codex with { Error = L.T("接続がタイムアウトしました") }; }
        catch (Exception e) { codex = codex with { Error = e.Message }; }
        finally
        {
            // Always clear busy, or one unexpected provider error would stop every later refresh.
            try { await Task.WhenAll(agyTask, grokTask, claudeTask); }
            catch (Exception) { }
            finally
            {
                busy = false;
                if (!closing) Apply();
                if (forceQueued && !closing) { forceQueued = false; _ = Refresh(force: true); }
            }
        }
    }
    private async Task RefreshAntigravity()
    {
        try { agy = await Providers.AntigravityLive(stop.Token); }
        catch (OperationCanceledException) { agy = agy with { Error = L.T("取得がタイムアウトしました") }; }
        catch (Exception e) { agy = agy with { Error = e.Message }; }
    }
    private async Task RefreshClaude(bool force)
    {
        // Prefer the account-wide API; fall back to the status line snapshot, keeping the last values grayed out on failure.
        try { claude = await ClaudeUsageApi.ReadLive(stop.Token, force); return; }
        catch (Exception e)
        {
            Reading? snapshot;
            try { snapshot = ClaudeCodeUsage.ReadSnapshot(); } catch (Exception) { snapshot = null; }
            var message = e is OperationCanceledException ? L.T("取得がタイムアウトしました")
                : e is IOException or HttpRequestException ? e.Message : L.T("Claudeの使用状況を取得できません");
            if (snapshot is { Quotas.Count: > 0 } && !snapshot.Stale) claude = snapshot;
            else if (claude is { Quotas.Count: > 0 }) claude = claude with { Error = message };
            else claude = snapshot is { Quotas.Count: > 0 } ? snapshot : new Reading("Claude Code", [], null, message);
        }
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
            displayedClaude = ClaudeCodeUsage.Waiting;
        // Show Grok whenever its CLI is installed, so a failed fetch explains itself instead of hiding the card.
        Reading? displayedGrok = grok.Quotas.Count > 0 || prefs.TaskbarTop == "Grok" || prefs.TaskbarBottom == "Grok" ||
            (grok.Error != null && grok.Error != L.T("Grok CLIが見つかりません")) ? grok : null;
        form.UpdateReadings(codex, agy, displayedClaude, displayedGrok, busy);
        var top = ForTaskbar(prefs.TaskbarTop, displayedClaude);
        var bottom = ForTaskbar(prefs.TaskbarBottom, displayedClaude);
        widget.UpdateReadings(top, bottom);
        form.PositionAtTray();
        shownTop = top; shownBottom = bottom;
        var extra = TooltipText.UpdateNotice(newerVersion) ?? L.T("クリックで詳細");
        tray.Text = TooltipText.For(TooltipText.Title, TooltipText.Status(top), extra);
        agyTray.Text = TooltipText.For(TooltipText.Title, TooltipText.Status(bottom), extra);
    }
    private Reading ForTaskbar(string provider, Reading? displayedClaude) => provider switch
    {
        "Antigravity" => agy,
        "Claude Code" => displayedClaude ?? ClaudeCodeUsage.Waiting,
        "Grok" => grok,
        _ => codex
    };
    protected override void ExitThreadCore()
    {
        closing = true; stop.Cancel(); poll.Dispose(); hoverClose.Dispose(); updateTimer.Dispose();
        showWait.Unregister(null); showSignal.Dispose();
        tray.Visible = false; agyTray.Visible = false;
        widget.Dispose(); tray.Icon?.Dispose(); tray.Dispose(); agyTray.Icon?.Dispose(); agyTray.Dispose(); menu.Dispose(); form.Dispose();
        foreach (var slot in extraSlots) { slot.Visible = false; slot.Icon?.Dispose(); slot.Dispose(); }
        base.ExitThreadCore();
    }
}
