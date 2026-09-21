using System.Text.Json;

namespace CodexLimitViewer;

internal static class L
{
    internal static bool English { get; set; }
    private static readonly Dictionary<string, string> Translations = Load();
    private static Dictionary<string, string> Load()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("CodexLimitViewer.English.json")!;
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
    internal static string For(Reading reading)
    {
        var stale = reading.Stale ? " (" + L.T("前回の値") + ")" : "";
        return L.English
            ? $"{reading.Provider} {reading.Compact} remaining{stale}\nClick for details"
            : $"{reading.Provider} 残り {reading.Compact}{stale}\nクリックで詳細";
    }
}
