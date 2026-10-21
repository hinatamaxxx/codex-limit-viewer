using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace UsageLimitViewer;

internal sealed record ReleaseInfo(Version Version, string ZipUrl, string ShaUrl);

// What the last check found, kept beside the cache so the menu can show it after a restart.
internal sealed class UpdateState
{
    public DateTimeOffset? LastCheck { get; set; }
    public int Failures { get; set; }
    public string? LastError { get; set; }
    public string? Latest { get; set; }
}

// Self-update from GitHub Releases: download the release zip, check it against the release's .sha256, move the
// running exe aside and put the new files in place, then restart. The per-user install folder needs no UAC.
internal static class Updater
{
    private const string Repository = "hinatamaxxx/usage-limit-viewer";
    internal const string AssetName = "usage-limit-viewer.zip";
    private const string DownloadPrefix = "https://github.com/" + Repository + "/releases/download/";
    internal const string ReleasePage = "https://github.com/" + Repository + "/releases/latest";
    internal static Version Current { get; } = Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0));
    private static string StatePath => Path.Combine(AppPaths.SharedDataDirectory, "update-state.json");
    private static string WorkFolder => Path.Combine(AppPaths.SharedDataDirectory, "update");

    internal static UpdateState LoadState()
    {
        try { return JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(StatePath)) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    internal static void SaveState(UpdateState state)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SharedDataDirectory);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    internal static Version? Newer(string tag, Version current) =>
        Version.TryParse(tag.TrimStart('v', 'V'), out var version) && Normalize(version) > current ? Normalize(version) : null;
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    private static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UsageLimitViewer/" + Current.ToString(3));
        return client;
    }

    // The latest release when it is newer than this app; null when this app is up to date.
    internal static async Task<ReleaseInfo?> FindNewer(CancellationToken token)
    {
        using var client = Client();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.GetAsync("https://api.github.com/repos/" + Repository + "/releases/latest", token);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String) throw new IOException("The release has no version tag.");
        if (Newer(tag.GetString()!, Current) is not { } version) return null;
        string? zip = null, sha = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                // Only files of this repository's own releases are ever downloaded.
                if (url == null || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) continue;
                if (name == AssetName) zip = url;
                else if (name == AssetName + ".sha256") sha = url;
            }
        if (zip == null || sha == null) throw new IOException(L.F("v{version} のリリースに更新用のファイルがありません", ("version", version.ToString(3))));
        return new ReleaseInfo(version, zip, sha);
    }

    // Puts the release in the folder this exe runs from. Returns the path of the new exe to restart into.
    internal static async Task<string> Install(ReleaseInfo release, CancellationToken token)
    {
        var target = Path.GetDirectoryName(Compat.ProcessPath)!;
        Directory.CreateDirectory(WorkFolder);
        var zipPath = Path.Combine(WorkFolder, AssetName);
        var staging = Path.Combine(WorkFolder, "v" + release.Version.ToString(3));
        using (var client = Client())
        {
            var expected = (await client.GetStringAsync(release.ShaUrl)).Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant()
                ?? throw new IOException("The checksum file is empty.");
            using (var response = await client.GetAsync(release.ZipUrl, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                using var output = File.Create(zipPath);
                await (await response.Content.ReadAsStreamAsync()).CopyToAsync(output, 81920, token);
            }
            string actual;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(zipPath))
                actual = string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
            if (actual != expected) throw new IOException(L.T("ダウンロードしたファイルのハッシュが一致しません"));
        }
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        using (var zip = ZipFile.OpenRead(zipPath))
            foreach (var entry in zip.Entries)
            {
                var output = Path.GetFullPath(Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!output.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("The update contains an invalid path.");
                if (entry.Name.Length == 0) { Directory.CreateDirectory(output); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                entry.ExtractToFile(output, true);
            }
        File.Delete(zipPath);
        var newExe = Path.Combine(staging, "UsageLimitViewer.exe");
        if (!File.Exists(newExe)) throw new IOException("The update has no UsageLimitViewer.exe.");
        // The exe inside must be the version the release says it is.
        var fileVersion = FileVersionInfo.GetVersionInfo(newExe).FileVersion;
        if (!Version.TryParse(fileVersion, out var staged) || Normalize(staged) != release.Version)
            throw new IOException(L.T("更新用のファイルの版がリリースと一致しません"));

        // The running exe cannot be overwritten but can be renamed; the next update removes the old copy.
        foreach (var old in Directory.GetFiles(target, "*.old-*")) try { File.Delete(old); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var marker = Path.Combine(target, ".updating");
        File.WriteAllText(marker, release.Version.ToString(3));
        try
        {
            foreach (var source in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, source.Substring(staging.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (File.Exists(destination) && destination.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    File.Move(destination, destination + ".old-" + DateTime.UtcNow.Ticks);
                File.Copy(source, destination, true);
            }
        }
        finally { File.Delete(marker); }
        Directory.Delete(staging, true);
        UpdateInstalledAppsEntry(target, release.Version);
        return Path.Combine(target, "UsageLimitViewer.exe");
    }

    // Keeps Settings → Apps → Installed apps showing the new version (the entry the setup created).
    private static void UpdateInstalledAppsEntry(string folder, Version version)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\UsageLimitViewer", writable: true);
            if (key?.GetValue("InstallLocation") is not string location ||
                !Path.GetFullPath(location).TrimEnd('\\').Equals(Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
            key.SetValue("DisplayVersion", version.ToString(3));
            key.SetValue("EstimatedSize", (int)(Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024), RegistryValueKind.DWord);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    internal static void OpenReleasePage()
    {
        try { Process.Start(new ProcessStartInfo(ReleasePage) { UseShellExecute = true })?.Dispose(); } catch { }
    }
}
