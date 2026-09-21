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
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        var exe = Directory.Exists(folder) ? Directory.GetFiles(folder, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        if (exe == null)
        {
            exe = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(p => Path.Combine(p, "codex.exe")).FirstOrDefault(File.Exists);
        }
        if (exe == null) throw new InvalidOperationException(L.T("Codexをインストールしてログインしてください"));
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("app-server");
        // stdio is the default transport on both older and current app-server versions.
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException(L.T("Codexを起動できません"));
        var errors = proc.StandardError.ReadToEndAsync();
        try
        {
            await proc.StandardInput.WriteLineAsync("""{"id":1,"method":"initialize","params":{"clientInfo":{"name":"codex-limit-viewer","version":"0.1.0"}}}""");
            await ReadResult(proc, 1, ct);
            await proc.StandardInput.WriteLineAsync("""{"method":"initialized","params":{}}""");
            await proc.StandardInput.WriteLineAsync("""{"id":2,"method":"account/rateLimits/read"}""");
            return QuotaParser.Codex(await ReadResult(proc, 2, ct));
        }
        finally
        {
            if (!proc.HasExited) proc.Kill(true);
            await proc.WaitForExitAsync(CancellationToken.None);
            await errors;
        }
    }
    private static async Task<JsonElement> ReadResult(Process proc, int id, CancellationToken ct)
    {
        while (await proc.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var rid) || !rid.TryGetInt32(out var n) || n != id) continue;
            if (root.TryGetProperty("error", out _)) throw new InvalidOperationException(L.T("Codexのログイン状態を確認してください"));
            return root.GetProperty("result").Clone();
        }
        throw new IOException(L.T("Codexとの接続が終了しました"));
    }
    public static async Task<Reading> AntigravityLive(CancellationToken stop)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
        if (!File.Exists(exe)) throw new IOException(L.T("agyが見つかりません"));
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
            return QuotaParser.AntigravityCommand(doc.RootElement);
        }
        finally
        {
            if (!proc.HasExited) proc.Kill(true);
            await proc.WaitForExitAsync(CancellationToken.None);
            await stderr;
        }
    }
}
