using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CodexLimitViewer;

// Reads the account-wide Claude quota with the Claude Code CLI sign-in, so it also reflects Claude Desktop usage.
// The token is only sent to api.anthropic.com and is never stored or logged by this app.
internal static class ClaudeUsageApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static string CredentialsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    internal static async Task<Reading> ReadLive(CancellationToken stop)
    {
        var token = ReadToken();
        if (token == null) throw new IOException(L.T("Claude Code CLIでログインしてください"));
        if (token.Value.Expires <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            // The CLI renews its own sign-in; running a read-only command lets it do so without us touching the refresh token.
            await RunCliStatus(stop);
            token = ReadToken();
            if (token == null || token.Value.Expires <= DateTimeOffset.UtcNow)
                throw new IOException(L.T("Claude Code CLIを一度起動してログインを更新してください"));
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value.Access);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        request.Headers.UserAgent.ParseAdd("CodexLimitViewer/" + typeof(ClaudeUsageApi).Assembly.GetName().Version?.ToString(3));
        using var response = await Http.SendAsync(request, stop);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new IOException(L.T("Claude Code CLIを一度起動してログインを更新してください"));
        if (!response.IsSuccessStatusCode) throw new IOException(L.T("Claudeの使用状況を取得できません"));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(stop));
        return Parse(doc.RootElement, DateTimeOffset.UtcNow);
    }

    private static (string Access, DateTimeOffset Expires)? ReadToken()
    {
        try
        {
            if (!File.Exists(CredentialsPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(CredentialsPath));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) || oauth.ValueKind != JsonValueKind.Object ||
                !oauth.TryGetProperty("accessToken", out var access) || access.ValueKind != JsonValueKind.String) return null;
            var expires = oauth.TryGetProperty("expiresAt", out var e) && e.TryGetInt64(out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : DateTimeOffset.MaxValue;
            return (access.GetString()!, expires);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentOutOfRangeException) { return null; }
    }

    private static async Task RunCliStatus(CancellationToken stop)
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        if (!File.Exists(exe))
            exe = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(p => Path.Combine(p, "claude.exe")).FirstOrDefault(File.Exists) ?? "";
        if (exe == "") return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("auth");
        psi.ArgumentList.Add("status");
        using var proc = Process.Start(psi);
        if (proc == null) return;
        proc.StandardInput.Close();
        var output = proc.StandardOutput.ReadToEndAsync();
        var errors = proc.StandardError.ReadToEndAsync();
        try { await proc.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { }
        finally
        {
            if (!proc.HasExited) proc.Kill(true);
            await proc.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, errors);
        }
    }

    internal static Reading Parse(JsonElement root, DateTimeOffset captured)
    {
        var rows = new List<Quota>();
        if (root.ValueKind == JsonValueKind.Object)
        {
            AddWindow(root, "five_hour", "5時間", rows);
            AddWindow(root, "seven_day", "週間", rows);
            AddWindow(root, "seven_day_opus", "週間 · Opus", rows);
            AddWindow(root, "seven_day_sonnet", "週間 · Sonnet", rows);
        }
        return new Reading("Claude Code", rows, captured,
            rows.Count == 0 ? L.T("Claude Codeの残量は利用できません") : null,
            L.T("Claude Code CLIのログイン経由"));
    }

    private static void AddWindow(JsonElement root, string key, string label, List<Quota> rows)
    {
        if (!root.TryGetProperty(key, out var window) || window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("utilization", out var used) || !used.TryGetDouble(out var percent) ||
            !double.IsFinite(percent) || percent < 0) return;
        DateTimeOffset? reset = null;
        if (window.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(r.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
        {
            if (at <= DateTimeOffset.UtcNow) return;
            reset = at;
        }
        // Model-specific weekly windows without a reset time are placeholders for unused models, not real limits.
        if (reset == null && key.StartsWith("seven_day_")) return;
        rows.Add(new(label, Math.Clamp(100 - percent, 0, 100), reset));
    }
}
