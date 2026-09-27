using System.Globalization;
using System.Drawing.Imaging;
using W = System.Windows;
using M = System.Windows.Media;
using I = System.Windows.Media.Imaging;

namespace CodexLimitViewer;

internal static class ClockTextRenderer
{
    internal static Bitmap Render(int width, int height, double dpi, Reading topReading, Reading bottomReading, bool hovered = false)
    {
        double scale = dpi / 96;
        double w = width / scale, h = height / scale;
        var visual = new M.DrawingVisual();
        M.TextOptions.SetTextFormattingMode(visual, M.TextFormattingMode.Display);
        M.TextOptions.SetTextRenderingMode(visual, M.TextRenderingMode.Grayscale);
        var typeface = new M.Typeface(new M.FontFamily("Segoe UI"), W.FontStyles.Normal, W.FontWeights.Normal, W.FontStretches.Normal);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new M.SolidColorBrush(M.Color.FromArgb(1, 0, 0, 0)), null, new W.Rect(0, 0, w, h));
            if (hovered)
                dc.DrawRoundedRectangle(new M.SolidColorBrush(M.Color.FromArgb(26, 255, 255, 255)),
                    null, new W.Rect(1, 5, w - 2, h - 10), 4, 4);
            const double row = 16;
            // Match the observed Windows 11 clock baselines (17/41 px at 150% DPI).
            double top = (h - 2 * row) / 2 - 4d / 3;
            Draw(topReading, top);
            Draw(bottomReading, top + row);
            void Draw(Reading reading, double y)
            {
                var label = Text(reading.Provider, M.Brushes.White);
                label.SetFontWeight(W.FontWeights.Normal);
                dc.DrawText(label, new W.Point(6, Snap(y + (row - label.Height) / 2)));
                M.Brush valueBrush = reading.Stale ? new M.SolidColorBrush(M.Color.FromRgb(190, 195, 207)) : M.Brushes.White;
                double right = w - 6;
                // Claude Code shows "weekly%(5-hour%)", e.g. "80%(20%)".
                var value = Text(TaskbarValue(reading), valueBrush);
                dc.DrawText(value, new W.Point(Snap(right - value.Width), Snap(y + (row - value.Height) / 2)));
            }
            double Snap(double value) => Math.Round(value * scale) / scale;
            M.FormattedText Text(string text, M.Brush brush, double size = 12) => new(text, CultureInfo.CurrentUICulture,
                W.FlowDirection.LeftToRight, typeface, size, brush, null, M.TextFormattingMode.Display, scale);
        }
        var target = new I.RenderTargetBitmap(width, height, dpi, dpi, M.PixelFormats.Pbgra32);
        target.Render(visual);
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try { target.CopyPixels(W.Int32Rect.Empty, data.Scan0, data.Stride * height, data.Stride); }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }

    internal static string TaskbarValue(Reading reading)
    {
        if (reading.Provider != "Claude Code") return reading.Compact;
        var five = reading.Quotas.FirstOrDefault(q => q.Label == "5時間");
        var week = reading.Quotas.FirstOrDefault(q => q.Label == "週間");
        return five != null && week != null ? $"{week.Remaining:0}%({five.Remaining:0}%)" : reading.Compact;
    }
}
