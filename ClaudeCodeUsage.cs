using System.Text.Json;

namespace CodexLimitViewer;

internal static class ClaudeCodeUsage
{
    internal static string SnapshotPath => Path.Combine(Preferences.Folder, "claude-code-usage.json");

    internal static Reading? ReadSnapshot()
    {
        if (!File.Exists(SnapshotPath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(SnapshotPath));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Claude Code snapshot must be an object.");
            var captured = root.TryGetProperty("captured_at", out var time) && time.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : new DateTimeOffset(File.GetLastWriteTimeUtc(SnapshotPath), TimeSpan.Zero);
            return Parse(root, captured);
        }
        catch (Exception e) when (e is IOException or JsonException or ArgumentOutOfRangeException)
        {
            return new Reading("Claude Code", [], null, L.T("Claude Codeの使用状況を読み取れません"));
        }
    }

    internal static Reading Parse(JsonElement root, DateTimeOffset captured)
    {
        var rows = new List<Quota>();
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("rate_limits", out var limits) && limits.ValueKind == JsonValueKind.Object)
        {
            AddWindow(limits, "five_hour", "5時間", rows);
            AddWindow(limits, "seven_day", "週間", rows);
        }
        return new Reading("Claude Code", rows, captured,
            rows.Count == 0 ? L.T("Claude Codeの残量は利用できません") : null,
            L.T("Claude Code statusLine経由"));
    }

    private static void AddWindow(JsonElement limits, string key, string label, List<Quota> rows)
    {
        if (!limits.TryGetProperty(key, out var window) || window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("used_percentage", out var used) || !used.TryGetDouble(out var percent) ||
            !double.IsFinite(percent) || percent < 0 || percent > 100) return;
        DateTimeOffset? reset = null;
        if (window.TryGetProperty("resets_at", out var r) && r.TryGetInt64(out var seconds))
        {
            try { reset = DateTimeOffset.FromUnixTimeSeconds(seconds); }
            catch (ArgumentOutOfRangeException) { return; }
            if (reset <= DateTimeOffset.UtcNow) return;
        }
        rows.Add(new(label, 100 - percent, reset));
    }
}
