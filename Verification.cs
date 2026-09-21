using System.Text.Json;

namespace CodexLimitViewer;

internal static class Verification
{
    internal static int Run()
    {
        var results = new List<string>();
        try
        {
            Check(DetailsForm.FormatCountdown(TimeSpan.FromHours(100)) == "リセットまで 4日 4時間 0分", "Countdown 100 hours uses days", results);
            Check(DetailsForm.FormatCountdown(TimeSpan.FromHours(24)) == "リセットまで 1日 0時間 0分", "Countdown day boundary", results);
            Check(DetailsForm.FormatCountdown(TimeSpan.FromMinutes(1439)) == "リセットまで 23時間 59分", "Countdown below one day", results);
            Check(DetailsForm.FormatCountdown(null) == "リセット時刻不明" && DetailsForm.FormatCountdown(TimeSpan.Zero) == "リセット時刻経過 · 更新待ち", "Countdown missing and expired", results);
            L.English = true;
            Check(DetailsForm.FormatCountdown(TimeSpan.FromHours(100)) == "Resets in 4d 4h 0m", "English duration", results);
            Check(L.F("取得 {age}", ("age", "09/22 03:00:00")) == "Fetched 09/22 03:00:00", "English timestamp", results);
            Check(L.QuotaLabel("週間") == "Weekly" && L.T("パネルを開く") == "Open panel", "English labels", results);
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
            results.Add("All tests passed.");
            var area = new Rectangle(0, 0, 1920, 1040);
            var anchor = new Rectangle(1700, 1045, 24, 24);
            var compact = TrayPresentation.Above(anchor, new Size(344, 54), area);
            var expanded = TrayPresentation.Above(anchor, new Size(460, 610), area);
            Check(compact.Y + 54 == expanded.Y + 610 && compact.Y + 54 < anchor.Top, "Tray anchored expansion", results);
            Check(TrayPresentation.Above(new Rectangle(1900, 1045, 24, 24), new Size(460, 610), area).X == 1460, "Right edge containment", results);
            Check(TrayPresentation.Above(new Rectangle(500, 0, 24, 24), new Size(460, 610), new Rectangle(0, 40, 1920, 1040)).Y >= 40, "Top taskbar containment", results);

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
        if (!menu.AutoClose || (GetWindowLongPtr(menu.Handle, -20).ToInt64() & 0x80) == 0 ||
            (GetWindowLongPtr(submenu.Handle, -20).ToInt64() & 0x80) == 0)
            throw new Exception("Tray menus must auto-close and never create taskbar buttons.");
        menu.Close();
        using var f = new DetailsForm(new Preferences());
        var c = new Reading("Codex", [new("5時間", 72, DateTimeOffset.UtcNow.AddHours(2)), new("週間", 43, DateTimeOffset.UtcNow.AddDays(3))], DateTimeOffset.UtcNow);
        var a = new Reading("Antigravity", [new("gemini-weekly", 86, DateTimeOffset.UtcNow.AddDays(4))], DateTimeOffset.UtcNow);
        if (live) { c = Task.Run(() => Providers.Codex(CancellationToken.None)).GetAwaiter().GetResult(); a = Task.Run(() => Providers.AntigravityLive(CancellationToken.None)).GetAwaiter().GetResult(); }
        f.UpdateReadings(c, a, false);
        f.Show(); f.Expand(false, false); Application.DoEvents();
        using (var bmp = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bmp, f.ClientRectangle); bmp.Save(Path.Combine(AppContext.BaseDirectory, "preview-compact.png")); }
        f.Expand(true, false); Application.DoEvents();
        using (var bmp = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bmp, f.ClientRectangle); bmp.Save(Path.Combine(AppContext.BaseDirectory, "preview-expanded.png")); }
        f.Hide(); return 0;
    }
}
