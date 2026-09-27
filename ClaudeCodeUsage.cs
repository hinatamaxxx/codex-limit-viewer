using System.Text.Json;

namespace CodexLimitViewer;

internal static class ClaudeCodeUsage
{
    internal static string SnapshotPath => Path.Combine(Preferences.Folder, "claude-code-usage.json");

    internal static string BridgePath => Path.Combine(Preferences.Folder, "ClaudeCodeStatusLine.ps1");
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    // EnableClaudeCode.ps1 points Claude Code at a copy of the bridge. Restore it when that copy disappears or is outdated,
    // otherwise the status line command fails silently and no quota ever arrives.
    internal static void EnsureBridge()
    {
        try
        {
            if (!File.Exists(SettingsPath) || !IsBridgeCommand(File.ReadAllText(SettingsPath))) return;
            using var stream = typeof(ClaudeCodeUsage).Assembly.GetManifestResourceStream("CodexLimitViewer.ClaudeCodeStatusLine.ps1")!;
            using var reader = new StreamReader(stream);
            var bridge = reader.ReadToEnd();
            if (File.Exists(BridgePath) && File.ReadAllText(BridgePath) == bridge) return;
            Directory.CreateDirectory(Preferences.Folder);
            File.WriteAllText(BridgePath, bridge, new System.Text.UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    internal static bool IsBridgeCommand(string settings)
    {
        try
        {
            using var doc = JsonDocument.Parse(settings, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("statusLine", out var line) && line.ValueKind == JsonValueKind.Object &&
                line.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String &&
                command.GetString()!.Replace('\\', '/').Contains("CodexLimitViewer/ClaudeCodeStatusLine.ps1", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException) { return false; }
    }

    // Shown as "—" until the first refresh finishes.
    internal static Reading Waiting => new("Claude Code", [], null);

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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentOutOfRangeException or InvalidOperationException)
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
