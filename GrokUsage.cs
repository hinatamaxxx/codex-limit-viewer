using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace CodexLimitViewer;

internal static class GrokUsage
{
    internal static async Task<Reading> ReadLive(CancellationToken stop)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return await ReadOnce(stop); }
            catch (GrokRpcException e) when (e.Code == -32603 && attempt == 0)
            {
                await Task.Delay(300, stop);
            }
        }
    }

    private static async Task<Reading> ReadOnce(CancellationToken stop)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok", "bin", "grok.exe");
        if (!File.Exists(exe))
            exe = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(p => Path.Combine(p, "grok.exe")).FirstOrDefault(File.Exists)
                ?? throw new IOException(L.T("Grok CLIが見つかりません"));

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        psi.ArgumentList.Add("agent");
        psi.ArgumentList.Add("stdio");
        using var proc = Process.Start(psi) ?? throw new IOException(L.T("Grok CLIを起動できません"));
        var stderr = proc.StandardError.ReadToEndAsync();
        try
        {
            await proc.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1,"clientCapabilities":{},"clientInfo":{"name":"CodexLimitViewer","version":"0.1.2"}}}""");
            await proc.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":2,"method":"_x.ai/billing","params":{}}""");
            await proc.StandardInput.FlushAsync(timeout.Token);
            while (true)
            {
                var line = await proc.StandardOutput.ReadLineAsync(timeout.Token)
                    ?? throw new IOException(L.T("Grok CLIとの接続が終了しました"));
                if (!line.StartsWith('{')) continue;
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var id) ||
                    id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var number) || number != 2) continue;
                if (root.TryGetProperty("error", out var error))
                {
                    if (error.TryGetProperty("code", out var code) && code.TryGetInt32(out var value) && value == -32601)
                        throw new IOException(L.T("Grok CLIを更新してください"));
                    if (error.TryGetProperty("code", out code) && code.TryGetInt32(out value))
                        throw new GrokRpcException(value, L.T("Grok CLIの利用状況を取得できません"));
                    throw new IOException(L.T("Grok CLIの利用状況を取得できません"));
                }
                if (!root.TryGetProperty("result", out var result))
                    throw new IOException(L.T("Grokの週次残量は利用できません"));
                return Parse(result, DateTimeOffset.UtcNow);
            }
        }
        finally
        {
            if (!proc.HasExited) proc.Kill(true);
            await proc.WaitForExitAsync(CancellationToken.None);
            await stderr;
        }
    }

    private sealed class GrokRpcException(int code, string message) : IOException(message)
    {
        internal int Code { get; } = code;
    }

    internal static Reading Parse(JsonElement result, DateTimeOffset captured)
    {
        if (!result.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object ||
            !config.TryGetProperty("creditUsagePercent", out var usage) || !usage.TryGetDouble(out var used) ||
            !double.IsFinite(used) || used < 0 ||
            !config.TryGetProperty("currentPeriod", out var period) || period.ValueKind != JsonValueKind.Object ||
            !period.TryGetProperty("type", out var type) || type.GetString() != "USAGE_PERIOD_TYPE_WEEKLY" ||
            !period.TryGetProperty("end", out var end) || end.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(end.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var reset) ||
            reset <= captured)
            throw new IOException(L.T("Grokの週次残量は利用できません"));
        return new Reading("Grok", [new Quota("週間", Math.Clamp(100 - used, 0, 100), reset)], captured,
            Source: L.T("Grok CLI経由"));
    }
}
