using System.Text.Json;

namespace CodexLimitViewer;

internal static class Verification
{
    internal static int Run()
    {
        var results = new List<string>();
        try
        {
            Check(!new Preferences().OpenDetailsOnHover, "Hover details defaults off", results);
            var display = new Preferences();
            Check(display.TaskbarTop == "Codex" && display.TaskbarBottom == "Antigravity", "Taskbar defaults to two providers", results);
            display.SelectTaskbarProvider(false, "Claude Code", persist: false);
            Check(display.TaskbarTop == "Codex" && display.TaskbarBottom == "Claude Code", "Taskbar row can show Claude Code", results);
            display.SelectTaskbarProvider(true, "Claude Code", persist: false);
            Check(display.TaskbarTop == "Claude Code" && display.TaskbarBottom == "Codex", "Selecting the other row swaps providers", results);
            display.SelectTaskbarProvider(false, "Grok", persist: false);
            Check(display.TaskbarTop == "Claude Code" && display.TaskbarBottom == "Grok", "Taskbar row can show Grok", results);
            var noon = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 27, 12, 0, 0)));
            Check(DetailsForm.FormatReset(noon.AddHours(4).AddMinutes(7), noon) == "今日 16:07 にリセット（あと4時間7分）", "Same-day reset shows today and time left", results);
            Check(DetailsForm.FormatReset(noon.AddHours(21), noon) == "明日 9:00 にリセット（あと21時間0分）", "Next-day reset shows tomorrow", results);
            Check(DetailsForm.FormatReset(noon.AddDays(6).AddHours(6), noon) == "10月3日(土) 18:00 にリセット（あと6日6時間）", "Later reset shows date and weekday", results);
            Check(DetailsForm.FormatReset(noon.AddSeconds(20), noon) == "今日 12:00 にリセット（あと1分）", "Under a minute never shows zero", results);
            Check(DetailsForm.FormatReset(null, noon) == "リセット時刻不明" && DetailsForm.FormatReset(noon, noon) == "リセット時刻経過 · 更新待ち", "Reset missing and expired", results);
            L.English = true;
            Check(DetailsForm.FormatReset(noon.AddDays(6).AddHours(6), noon) == "Resets Sat, Oct 3 18:00 (in 6d 6h)", "English reset date", results);
            Check(DetailsForm.FormatReset(noon.AddHours(4).AddMinutes(7), noon) == "Resets today 16:07 (in 4h 7m)", "English same-day reset", results);
            Check(L.F("取得 {age}", ("age", "09/22 03:00:00")) == "Fetched 09/22 03:00:00", "English timestamp", results);
            Check(L.QuotaLabel("週間") == "Weekly" && L.T("パネルを開く") == "Open panel" &&
                L.T("Codexアプリ経由") == "Via Codex app" &&
                L.T("Grok CLI経由") == "Via Grok CLI" &&
                L.T("ホバーで詳細を開く") == "Open details on hover", "English labels", results);
            L.English = false;
            using var codex = JsonDocument.Parse("""{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":22.5,"windowDurationMins":300,"resetsAt":1800000000},"secondary":{"usedPercent":90,"windowDurationMins":10080}},"extra":{"primary":{"usedPercent":15,"windowDurationMins":60}}}}""");
            var c = QuotaParser.Codex(codex.RootElement);
            Check(c.Quotas.Count == 3 && c.Quotas[0].Remaining == 77.5 && c.Quotas[1].Label == "週間", "Codex multiple windows and fractional percentage", results);
            using var command = JsonDocument.Parse("""{"status":"SUCCESS","command":{"name":"usage","data":{"groups":[{"buckets":[{"id":"gemini-weekly","remaining_fraction":0.9974,"reset_time":"2026-10-01T00:00:00Z"},{"id":"gemini-5h","remaining_fraction":1}]},{"buckets":[{"id":"3p-weekly","remaining_fraction":0}]}]}}}""");
            var live = QuotaParser.AntigravityCommand(command.RootElement);
            Check(live.Quotas.Count == 3 && Math.Abs(live.Quotas[0].Remaining - 99.74) < .001 && live.Quotas[2].Remaining == 0, "AGY command groups retain precise quotas", results);
            using var badCommand = JsonDocument.Parse("""{"status":"ERROR"}""");
            bool rejected = false;
            try { QuotaParser.AntigravityCommand(badCommand.RootElement); } catch (IOException) { rejected = true; }
            Check(rejected, "Failed AGY command never becomes fresh quota", results);
            using var legacy = JsonDocument.Parse("""{"rateLimits":{"primary":{"usedPercent":0,"windowDurationMins":300}}}""");
            Check(QuotaParser.Codex(legacy.RootElement).Quotas.Single().Remaining == 100, "Codex legacy response", results);
            using var empty = JsonDocument.Parse("""{"rateLimits":{"primary":null}}""");
            Check(QuotaParser.Codex(empty.RootElement).Quotas.Count == 0, "Missing quota is not zero or unlimited", results);
            using var agy = JsonDocument.Parse("""{"quota":{"gemini-weekly":{"remaining_fraction":0.9378,"reset_time":"2026-10-01T00:00:00Z"},"missing":{},"invalid":{"remaining_fraction":2},"zero":{"remaining_fraction":0}}}""");
            var a = QuotaParser.Antigravity(agy.RootElement, DateTimeOffset.UtcNow.AddMinutes(-11));
            Check(a.Quotas.Count == 2 && Math.Abs(a.Quotas[0].Remaining - 93.78) < .001 && a.Quotas[1].Remaining == 0, "AGY percentage and invalid data", results);
            Check(a.Stale && !c.Stale && (c with { Error = "offline" }).Stale, "Stale and failed refresh state", results);
            using var claudeData = JsonDocument.Parse("""{"rate_limits":{"five_hour":{"used_percentage":23.5,"resets_at":2200000000},"seven_day":{"used_percentage":41.2,"resets_at":2200000000}}}""");
            var claude = ClaudeCodeUsage.Parse(claudeData.RootElement, DateTimeOffset.UtcNow);
            Check(claude.Quotas.Count == 2 && Math.Abs(claude.Quotas[0].Remaining - 76.5) < .001 && Math.Abs(claude.Quotas[1].Remaining - 58.8) < .001,
                "Claude Code status line usage becomes remaining quota", results);
            using var freeClaudeData = JsonDocument.Parse("""{"rate_limits":{}}""");
            Check(ClaudeCodeUsage.Parse(freeClaudeData.RootElement, DateTimeOffset.UtcNow).Quotas.Count == 0,
                "Missing Claude Code limits never become zero usage", results);
            using var expiredClaudeData = JsonDocument.Parse("""{"rate_limits":{"five_hour":{"used_percentage":20,"resets_at":1000000000}}}""");
            Check(ClaudeCodeUsage.Parse(expiredClaudeData.RootElement, DateTimeOffset.UtcNow).Quotas.Count == 0,
                "Expired Claude Code limits are not displayed", results);
            using var grokData = JsonDocument.Parse("""{"config":{"creditUsagePercent":27.5,"currentPeriod":{"type":"USAGE_PERIOD_TYPE_WEEKLY","end":"2026-10-01T00:00:00Z"}}}""");
            var grok = GrokUsage.Parse(grokData.RootElement, new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
            Check(grok.Quotas.Count == 1 && grok.Quotas[0].Remaining == 72.5 && grok.Quotas[0].Label == "週間" && grok.Quotas[0].Reset?.UtcDateTime == new DateTime(2026, 10, 1),
                "Grok weekly usage becomes remaining quota and reset date", results);
            using var grokMonthly = JsonDocument.Parse("""{"config":{"creditUsagePercent":27.5,"currentPeriod":{"type":"USAGE_PERIOD_TYPE_MONTHLY","end":"2026-10-01T00:00:00Z"}}}""");
            bool invalidGrok = false;
            try { GrokUsage.Parse(grokMonthly.RootElement, DateTimeOffset.UtcNow); } catch (IOException) { invalidGrok = true; }
            Check(invalidGrok, "Grok non-weekly data is not mislabeled", results);
            var area = new Rectangle(0, 0, 1920, 1040);
            var anchor = new Rectangle(1700, 1045, 24, 24);
            var compact = TrayPresentation.Above(anchor, new Size(344, 54), area);
            var expanded = TrayPresentation.Above(anchor, new Size(460, 610), area);
            Check(compact.Y + 54 == expanded.Y + 610 && compact.Y + 54 < anchor.Top, "Tray anchored expansion", results);
            Check(TrayPresentation.Above(new Rectangle(1900, 1045, 24, 24), new Size(460, 610), area).X == 1460, "Right edge containment", results);
            Check(TrayPresentation.Above(new Rectangle(500, 0, 24, 24), new Size(460, 610), new Rectangle(0, 40, 1920, 1040)).Y >= 40, "Top taskbar containment", results);
            Check(ClaudeCodeUsage.IsBridgeCommand("""{"statusLine":{"type":"command","command":"powershell -File \"C:\\Users\\a\\AppData\\Local\\CodexLimitViewer\\ClaudeCodeStatusLine.ps1\""}}""") &&
                !ClaudeCodeUsage.IsBridgeCommand("""{"statusLine":{"type":"command","command":"other.ps1"}}""") &&
                !ClaudeCodeUsage.IsBridgeCommand("not json"), "Claude Code bridge command is recognized", results);
            Check(ClaudeCodeUsage.Waiting.Quotas.Count == 0 && ClaudeCodeUsage.Waiting.Compact == "—",
                "Claude Code waiting state never becomes zero", results);
            using var claudeApi = JsonDocument.Parse("""{"five_hour":{"utilization":12.5,"resets_at":"2099-01-01T00:00:00+00:00"},"seven_day":{"utilization":40,"resets_at":"2099-01-02T00:00:00Z"},"seven_day_opus":{"utilization":0,"resets_at":null},"seven_day_oauth_apps":null}""");
            var claudeLive = ClaudeUsageApi.Parse(claudeApi.RootElement, DateTimeOffset.UtcNow);
            Check(claudeLive.Quotas.Count == 2 && claudeLive.Quotas[0].Remaining == 87.5 && claudeLive.Quotas[1].Label == "週間" &&
                claudeLive.Quotas[1].Remaining == 60 && claudeLive.Error == null, "Claude usage API becomes remaining quota", results);
            using var claudeEmpty = JsonDocument.Parse("""{"five_hour":null}""");
            Check(ClaudeUsageApi.Parse(claudeEmpty.RootElement, DateTimeOffset.UtcNow).Quotas.Count == 0, "Missing Claude usage never becomes zero", results);
            var order = new Preferences();
            order.SelectTaskbarProvider(true, "Claude Code", persist: false);
            order.SelectTaskbarProvider(false, "Grok", persist: false);
            var sorted = DetailsForm.OrderedReadings(order, new("Codex", [], null), new("Antigravity", [], null), null, new("Claude Code", [], null), new("Grok", [], null)).Select(r => r.Provider).ToArray();
            Check(sorted.SequenceEqual(new[] { "Claude Code", "Grok", "Codex", "Antigravity" }), "Details list starts with taskbar providers", results);
            results.Add("All tests passed.");

            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "test-results.txt"), results); return 0;
        }
        catch (Exception ex)
        {
            results.Add(ex.ToString()); File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "test-results.txt"), results); return 1;
        }
    }
    private static void Check(bool value, string name, List<string> results)
    {
        if (!value) throw new Exception(name); results.Add("PASS " + name);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    internal static int Render(bool live = false)
    {
        using var menu = new TrayContextMenu();
        using var submenu = new TraySubMenu();
        menu.Items.Add("Menu test");
        menu.Show(new Point(0, 0));
        Application.DoEvents();
        if (!menu.AutoClose || !submenu.AutoClose || (GetWindowLongPtr(menu.Handle, -20).ToInt64() & 0x80) == 0 ||
            (GetWindowLongPtr(submenu.Handle, -20).ToInt64() & 0x80) == 0)
            throw new Exception("Tray menus must stay open for settings changes and never create taskbar buttons.");
        var anchor = new Rectangle(800, 800, 192, 72);
        menu.TrayAnchor = () => anchor;
        menu.Close();
        menu.Show(new Point(10, 10));
        Application.DoEvents();
        var cursorBeforeClick = Cursor.Position;
        Cursor.Position = new Point(menu.Left + 5, menu.Top + 5);
        menu.Items[0].PerformClick();
        Application.DoEvents();
        var stayedOpen = menu.Visible;
        Cursor.Position = cursorBeforeClick;
        if (!stayedOpen) throw new Exception("Clicking a settings item must keep the menu open.");
        var firstPosition = menu.Location;
        menu.ObservePointer(new Point(menu.Left + 5, menu.Top + 5), false);
        menu.ObservePointer(new Point(menu.Left + 5, menu.Top + 5), true);
        if (!menu.Visible) throw new Exception("An inside click must not dismiss the menu.");
        menu.ObservePointer(Point.Empty, false);
        menu.ObservePointer(Point.Empty, true);
        if (menu.Visible) throw new Exception("An outside click must dismiss the menu.");
        menu.Show(new Point(400, 400));
        Application.DoEvents();
        if (menu.Location != firstPosition) throw new Exception("Menu position must not depend on click position.");
        menu.Close();
        using var f = new DetailsForm(new Preferences());
        var c = new Reading("Codex", [new("5時間", 72, DateTimeOffset.UtcNow.AddHours(2)), new("週間", 43, DateTimeOffset.UtcNow.AddDays(3))], DateTimeOffset.UtcNow, Source: L.T("Codexアプリ経由"));
        var a = new Reading("Antigravity", [new("gemini-weekly", 86, DateTimeOffset.UtcNow.AddDays(4))], DateTimeOffset.UtcNow, Source: L.T("agy CLI経由"));
        if (live) { c = Task.Run(() => Providers.Codex(CancellationToken.None)).GetAwaiter().GetResult(); a = Task.Run(() => Providers.AntigravityLive(CancellationToken.None)).GetAwaiter().GetResult(); }
        f.UpdateReadings(c, a, null, null, false);
        f.Show(); f.Expand(false, false); Application.DoEvents();
        using (var bmp = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bmp, f.ClientRectangle); bmp.Save(Path.Combine(AppContext.BaseDirectory, "preview-compact.png")); }
        f.Expand(true, false); Application.DoEvents();
        using (var bmp = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bmp, f.ClientRectangle); bmp.Save(Path.Combine(AppContext.BaseDirectory, "preview-expanded.png")); }
        using (var hover = ClockTextRenderer.Render(192, 72, 144, c, a, true))
            hover.Save(Path.Combine(AppContext.BaseDirectory, "preview-hover.png"));
        var exampleClaude = new Reading("Claude Code", [new("5時間", 76.5, DateTimeOffset.UtcNow.AddHours(3))], DateTimeOffset.UtcNow);
        using (var selectedClaude = ClockTextRenderer.Render(192, 72, 144, c, exampleClaude))
            selectedClaude.Save(Path.Combine(AppContext.BaseDirectory, "preview-claude.png"));
        var exampleGrok = new Reading("Grok", [new("週間", 73, DateTimeOffset.UtcNow.AddDays(4))], DateTimeOffset.UtcNow, Source: L.T("Grok CLI経由"));
        f.UpdateReadings(c, a, null, exampleGrok, false);
        f.Expand(true, false); Application.DoEvents();
        using (var grokDetails = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(grokDetails, f.ClientRectangle); grokDetails.Save(Path.Combine(AppContext.BaseDirectory, "preview-grok-details.png")); }
        using (var selectedGrok = ClockTextRenderer.Render(192, 72, 144, c, exampleGrok))
            selectedGrok.Save(Path.Combine(AppContext.BaseDirectory, "preview-grok-taskbar.png"));
        f.Hide(); return 0;
    }
}
