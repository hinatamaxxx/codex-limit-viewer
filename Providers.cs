using System.Diagnostics;
using System.Text.Json;

namespace CodexLimitViewer;

internal static class Providers
{
    public static async Task<Reading> Codex(CancellationToken stop)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        var ct = timeout.Token;
        var (exe, desktopApp) = FindCodex();
        if (exe == null) throw new InvalidOperationException(L.T("Codexをインストールしてログインしてください"));
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("app-server");
        // stdio is the default transport on both older and current app-server versions.
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException(L.T("Codexを起動できません"));
        var errors = proc.StandardError.ReadToEndAsync();
        try
        {
            await proc.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "codex-limit-viewer", version = typeof(Providers).Assembly.GetName().Version?.ToString(3) } } }));
            await ReadResult(proc, 1, ct);
            await proc.StandardInput.WriteLineAsync("""{"method":"initialized","params":{}}""");
            await proc.StandardInput.WriteLineAsync("""{"id":2,"method":"account/rateLimits/read"}""");
            return QuotaParser.Codex(await ReadResult(proc, 2, ct)) with
            {
                Source = L.T(desktopApp ? "Codexアプリ経由" : "Codex CLI経由")
            };
        }
        finally
        {
            if (!proc.HasExited) proc.Kill(true);
            await proc.WaitForExitAsync(CancellationToken.None);
            await errors;
        }
    }
    // The desktop app's bundled codex.exe first, then a codex.exe on PATH, then the native binary behind an npm
    // install, whose PATH entry is only a codex.cmd script that cannot speak to us over stdio itself.
    internal static (string? Exe, bool DesktopApp) FindCodex() => FindCodex(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin"),
        Environment.GetEnvironmentVariable("PATH") ?? "",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
    internal static (string? Exe, bool DesktopApp) FindCodex(string folder, string pathVariable, string defaultNpm)
    {
        var bundled = Directory.Exists(folder) ? Directory.GetFiles(folder, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        if (bundled != null) return (bundled, true);
        var path = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var onPath = path.Select(p => Path.Combine(p, "codex.exe")).FirstOrDefault(File.Exists);
        if (onPath != null) return (onPath, false);
        var npmFolders = path.Where(p => File.Exists(Path.Combine(p, "codex.cmd")))
            .Append(defaultNpm);
        foreach (var npm in npmFolders)
        {
            var vendor = Path.Combine(npm, "node_modules", "@openai", "codex", "vendor");
            if (!Directory.Exists(vendor)) continue;
            var native = Directory.GetFiles(vendor, "codex.exe", SearchOption.AllDirectories)
                .OrderByDescending(f => f.Contains("x86_64", StringComparison.OrdinalIgnoreCase) == Environment.Is64BitOperatingSystem).FirstOrDefault();
            if (native != null) return (native, false);
        }
        return (null, false);
    }
    private static async Task<JsonElement> ReadResult(Process proc, int id, CancellationToken ct)
    {
        while (await proc.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            // Skip log lines; only JSON-RPC messages are answers.
            if (!line.TrimStart().StartsWith('{')) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var rid) || !rid.TryGetInt32(out var n) || n != id) continue;
            if (root.TryGetProperty("error", out _)) throw new InvalidOperationException(L.T("Codexのログイン状態を確認してください"));
            return root.GetProperty("result").Clone();
        }
        throw new IOException(L.T("Codexとの接続が終了しました"));
    }
    // Set once agy answers /usage by calling the model instead of running the command, so we never spend quota by polling.
    private static bool agyUsageSpendsQuota;
    public static async Task<Reading> AntigravityLive(CancellationToken stop)
    {
        if (agyUsageSpendsQuota) throw new IOException(L.T("このagyでは /usage がモデル呼び出しになるため、残量取得を停止しています"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
        if (!File.Exists(exe))
            exe = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => Path.Combine(p, "agy.exe")).FirstOrDefault(File.Exists) ?? throw new IOException(L.T("agyが見つかりません"));
        Directory.CreateDirectory(Preferences.Folder);
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = true, WorkingDirectory = Preferences.Folder
        };
        foreach (var arg in new[] { "-p", "/usage", "--output-format", "json" }) psi.ArgumentList.Add(arg);
        using var proc = Process.Start(psi) ?? throw new IOException(L.T("agyを起動できません"));
        proc.StandardInput.Close();
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        try
        {
            await proc.WaitForExitAsync(timeout.Token);
            if (proc.ExitCode != 0) throw new IOException(L.T("agyの残量取得に失敗しました。ログイン状態を確認してください"));
            using var doc = JsonDocument.Parse(await stdout);
            if (QuotaParser.AgyRanModel(doc.RootElement))
            {
                agyUsageSpendsQuota = true;
                throw new IOException(L.T("このagyでは /usage がモデル呼び出しになるため、残量取得を停止しています"));
            }
            return QuotaParser.AntigravityCommand(doc.RootElement) with { Source = L.T("agy CLI経由") };
        }
        finally
        {
            if (!proc.HasExited) proc.Kill(true);
            await proc.WaitForExitAsync(CancellationToken.None);
            await stderr;
        }
    }
}
