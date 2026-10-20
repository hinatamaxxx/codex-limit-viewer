using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace CodexLimitViewer;

// Only notifies: the user downloads the new setup from the release page, which lists SHA-256 checksums.
internal static class UpdateCheck
{
    internal const string ReleasePage = "https://github.com/hinatamaxxx/codex-limit-viewer/releases/latest";
    private const string LatestRelease = "https://api.github.com/repos/hinatamaxxx/codex-limit-viewer/releases/latest";
    internal static Version Current { get; } = Normalize(typeof(UpdateCheck).Assembly.GetName().Version ?? new Version(0, 0, 0));

    internal static async Task<Version?> FindNewer(CancellationToken token)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexLimitViewer/" + Current.ToString(3));
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.GetAsync(LatestRelease, token);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return doc.RootElement.TryGetProperty("tag_name", out var tag) && tag.ValueKind == JsonValueKind.String
            ? Newer(tag.GetString()!, Current) : null;
    }

    internal static Version? Newer(string tag, Version current) =>
        Version.TryParse(tag.TrimStart('v', 'V'), out var version) && Normalize(version) > current ? Normalize(version) : null;

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    internal static void OpenReleasePage()
    {
        try { Process.Start(new ProcessStartInfo(ReleasePage) { UseShellExecute = true })?.Dispose(); } catch { }
    }
}
