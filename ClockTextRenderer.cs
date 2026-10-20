using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace CodexLimitViewer;

internal static class ClockTextRenderer
{
    // Grayscale-antialiased GDI+ text: ClearType needs an opaque background, and this surface is a transparent layered window.
    internal static Bitmap Render(int width, int height, double dpi, Reading topReading, Reading bottomReading, bool hovered = false)
    {
        float scale = (float)(dpi / 96);
        float w = width / scale, h = height / scale;
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        // Nearly transparent instead of fully transparent, so the whole surface still receives the mouse.
        g.Clear(Color.FromArgb(1, 0, 0, 0));
        g.ScaleTransform(scale, scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        if (hovered)
        {
            using var hover = new SolidBrush(Color.FromArgb(26, 255, 255, 255));
            using var shape = DetailsForm.Rounded(new RectangleF(1, 5, w - 2, h - 10), 4);
            g.FillPath(hover, shape);
        }
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        const float row = 16;
        // Match the observed Windows 11 clock baselines (17/41 px at 150% DPI).
        float top = (h - 2 * row) / 2 - 4f / 3;
        Draw(topReading, top);
        Draw(bottomReading, top + row);
        return bitmap;

        void Draw(Reading reading, float y)
        {
            using var labelFont = Font(12);
            string name = TaskbarName(reading.Provider);
            var label = g.MeasureString(name, labelFont, PointF.Empty, format);
            float lineHeight = LineHeight(labelFont);
            g.DrawString(name, labelFont, Brushes.White, 6, Snap(y + (row - lineHeight) / 2), format);
            using var valueBrush = new SolidBrush(reading.Stale ? Color.FromArgb(190, 195, 207) : Color.White);
            float right = w - 6;
            // Both windows read "5-hour%(weekly%)" by default, e.g. "20%(80%)"; shrink the value rather than overlap the label.
            var text = TaskbarValue(reading);
            float size = 12;
            var valueFont = Font(size);
            var value = g.MeasureString(text, valueFont, PointF.Empty, format);
            for (size = 11.5f; size >= 9 && 6 + label.Width + 4 + value.Width > right; size -= 0.5f)
            {
                valueFont.Dispose(); valueFont = Font(size);
                value = g.MeasureString(text, valueFont, PointF.Empty, format);
            }
            using (valueFont)
                g.DrawString(text, valueFont, valueBrush, Snap(right - value.Width), Snap(y + (row - LineHeight(valueFont)) / 2), format);
        }
        float Snap(float value) => (float)Math.Round(value * scale) / scale;
        static Font Font(float size) => new("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel);
        // The font's line spacing in pixels, like WPF's FormattedText.Height, so text sits where it did before.
        static float LineHeight(Font font) =>
            font.Size * font.FontFamily.GetLineSpacing(font.Style) / font.FontFamily.GetEmHeight(font.Style);
    }

    // "Claude Code" leaves too little room for "80%(20%)" in the four-slot widget.
    internal static string TaskbarName(string provider) => provider == "Claude Code" ? "Claude" : provider;
    internal static bool FiveHourFirst { get; set; } = true;
    internal static string TaskbarValue(Reading reading)
    {
        // Services with both a 5-hour and a weekly window (Claude Code, Codex on some plans) show both.
        var five = reading.Quotas.FirstOrDefault(q => q.Label == "5時間");
        var week = reading.Quotas.FirstOrDefault(q => q.Label == "週間");
        if (five == null || week == null) return reading.Compact;
        return FiveHourFirst ? $"{five.Remaining:0}%({week.Remaining:0}%)" : $"{week.Remaining:0}%({five.Remaining:0}%)";
    }
}
