using System.Text.Json;

namespace UsageLimitViewer;

internal static class L
{
    internal static bool English { get; set; }
    private static readonly Dictionary<string, string> Translations = Load();
    private static Dictionary<string, string> Load()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("UsageLimitViewer.English.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    internal static string T(string text) => English && Translations.TryGetValue(text, out var value) ? value : text;
    internal static string F(string template, params (string Name, object Value)[] values)
    {
        var result = T(template);
        foreach (var (name, value) in values) result = result.Replace("{" + name + "}", Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
        return result;
    }
    internal static string QuotaLabel(string label) => English ? label.Replace("週間", T("週間")).Replace("5時間", T("5時間")).Replace("時間", " hours") : label;
}

internal static class TooltipText
{
    internal const int Limit = 63; // NotifyIcon.Text maximum.
    internal static string Title => "Usage Limit Viewer " + Updater.Current.ToString(3);

    internal static string Status(Reading reading)
    {
        var stale = reading.Stale ? " (" + L.T("前回の値") + ")" : "";
        return L.English
            ? $"{ClockTextRenderer.TaskbarName(reading.Provider)} {ClockTextRenderer.TaskbarValue(reading)} remaining{stale}"
            : $"{ClockTextRenderer.TaskbarName(reading.Provider)} 残り {ClockTextRenderer.TaskbarValue(reading)}{stale}";
    }


    // App and version, then status, then one extra line; lower lines are dropped to stay within the limit.
    internal static string For(params string?[] lines)
    {
        var kept = lines.Where(l => !string.IsNullOrEmpty(l)).ToList();
        while (kept.Count > 1 && string.Join("\n", kept).Length > Limit) kept.RemoveAt(kept.Count - 1);
        var text = string.Join("\n", kept);
        return text.Length > Limit ? text.Substring(0, Limit) : text;
    }
}
