using System.Globalization;
using System.Drawing.Imaging;
using W = System.Windows;
using M = System.Windows.Media;
using I = System.Windows.Media.Imaging;

namespace CodexLimitViewer;

internal static class ClockTextRenderer
{
    internal static Bitmap Render(int width, int height, double dpi, Reading codex, Reading agy)
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
            const double row = 16;
            // Match the observed Windows 11 clock baselines (17/41 px at 150% DPI).
            double top = (h - 2 * row) / 2 - 4d / 3;
            Draw(codex, top);
            Draw(agy, top + row);
            void Draw(Reading reading, double y)
            {
                var label = Text(reading.Provider, M.Brushes.White);
                label.SetFontWeight(W.FontWeights.Normal);
                var value = Text(reading.Compact, reading.Stale ? new M.SolidColorBrush(M.Color.FromRgb(190, 195, 207)) : M.Brushes.White);
                dc.DrawText(label, new W.Point(6, Math.Round((y + (row - label.Height) / 2) * scale) / scale));
                dc.DrawText(value, new W.Point(Math.Round((w - 6 - value.Width) * scale) / scale,
                    Math.Round((y + (row - value.Height) / 2) * scale) / scale));
            }
            M.FormattedText Text(string text, M.Brush brush) => new(text, CultureInfo.CurrentUICulture,
                W.FlowDirection.LeftToRight, typeface, 12, brush, null, M.TextFormattingMode.Display, scale);
        }
        var target = new I.RenderTargetBitmap(width, height, dpi, dpi, M.PixelFormats.Pbgra32);
        target.Render(visual);
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try { target.CopyPixels(W.Int32Rect.Empty, data.Scan0, data.Stride * height, data.Stride); }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }
}
