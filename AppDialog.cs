using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace UsageLimitViewer;

// A Windows 11 style dialog: text on top, a tinted button band below. Used to confirm exiting and to finish installing.
// The default button (Enter, and where Windows' "snap pointer to default button" lands) is the button focused first:
// Cancel when there is one, so a stray Enter never closes the app.
internal sealed class AppDialog : Form
{
    private readonly bool dark = IsDarkTheme();
    private readonly Label heading = new(), body = new();
    private readonly DialogButton primary = new() { Primary = true, DialogResult = DialogResult.OK };
    private readonly DialogButton? cancel;
    private readonly DialogCheck? check;
    private int bandTop;
    private Color Surface => dark ? Color.FromArgb(39, 39, 39) : Color.White;
    private Color Band => dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
    private Color Ink => dark ? Color.White : Color.FromArgb(27, 27, 27);
    private Color Muted => dark ? Color.FromArgb(206, 206, 206) : Color.FromArgb(68, 68, 68);
    internal bool Checked => check?.Checked ?? false;

    internal AppDialog(string headingText, string bodyText, string primary, string? cancel = null, string? check = null, bool checkedByDefault = true)
    {
        Text = "Usage Limit Viewer";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None; // Laid out from the system DPI.
        TopMost = true;
        try { Icon = Icon.ExtractAssociatedIcon(Compat.ProcessPath); } catch { }
        BackColor = Surface;
        heading.Text = headingText;
        body.Text = bodyText;
        heading.ForeColor = Ink; body.ForeColor = Muted;
        heading.BackColor = body.BackColor = Surface;
        this.primary.Text = primary;
        this.primary.Dark = dark;
        Controls.AddRange([heading, body, this.primary]);
        if (check != null)
        {
            this.check = new DialogCheck { Text = check, Checked = checkedByDefault, Dark = dark, BackColor = Surface, ForeColor = Ink };
            Controls.Add(this.check);
        }
        if (cancel != null)
        {
            this.cancel = new DialogButton { Text = cancel, DialogResult = DialogResult.Cancel, Dark = dark };
            Controls.Add(this.cancel);
            CancelButton = this.cancel;
        }
        else CancelButton = this.primary;
        // Buttons sit directly on the form (the band is only painted), so the pointer snaps to the right place.
        AcceptButton = (IButtonControl?)this.cancel ?? this.primary;
        LayoutDialog();
    }

    private static bool IsDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch { return false; }
    }

    private void LayoutDialog()
    {
        float scale = Compat.SystemDpi / 96f;
        int S(float value) => (int)Math.Round(value * scale);
        string family = FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        heading.Font = new Font("Segoe UI Semibold", 20 * scale, GraphicsUnit.Pixel);
        body.Font = new Font(family, 14 * scale, GraphicsUnit.Pixel);
        primary.Font = new Font(family, 14 * scale, GraphicsUnit.Pixel);
        if (cancel != null) cancel.Font = primary.Font;
        int width = S(460), inner = width - S(56);
        var bodySize = TextRenderer.MeasureText(body.Text, body.Font, new Size(inner, int.MaxValue), TextFormatFlags.WordBreak);
        heading.SetBounds(S(28), S(26), inner, S(30));
        body.SetBounds(S(28), S(26 + 30 + 12), inner, bodySize.Height + S(4));
        int y = body.Bottom;
        if (check != null)
        {
            check.Font = body.Font;
            check.SetBounds(S(28), y + S(16), inner, S(28));
            y = check.Bottom;
        }
        bandTop = y + S(28);
        int bandHeight = S(18 + 32 + 18), buttonTop = bandTop + S(18);
        int right = width - S(28);
        if (cancel != null)
        {
            cancel.SetBounds(right - S(112), buttonTop, S(112), S(32));
            right = cancel.Left - S(8);
        }
        primary.SetBounds(right - S(112), buttonTop, S(112), S(32));
        ClientSize = new Size(width, bandTop + bandHeight);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        using var band = new SolidBrush(Band);
        e.Graphics.FillRectangle(band, 0, bandTop, ClientSize.Width, ClientSize.Height - bandTop);
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int value = dark ? 1 : 0;
        DwmSetWindowAttribute(Handle, 20, ref value, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); ((Control?)cancel ?? primary).Focus(); }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private sealed class DialogButton : Button
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool Primary { get; init; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool Dark { get; set; }
        private bool hovered, pressed;
        internal DialogButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            GotFocus += (_, _) => Invalidate();
            LostFocus += (_, _) => Invalidate();
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            // The band colour behind the rounded corners.
            e.Graphics.Clear(Dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // Windows' default accent blue for the main action; exiting is not dangerous, so it is never red.
            Color fill = Primary
                ? Dark ? pressed ? Color.FromArgb(66, 158, 205) : hovered ? Color.FromArgb(89, 183, 232) : Color.FromArgb(76, 194, 255)
                       : pressed ? Color.FromArgb(0, 84, 153) : hovered ? Color.FromArgb(0, 95, 170) : Color.FromArgb(0, 103, 192)
                : Dark ? pressed ? Color.FromArgb(39, 39, 39) : hovered ? Color.FromArgb(61, 61, 61) : Color.FromArgb(55, 55, 55)
                       : pressed ? Color.FromArgb(240, 240, 240) : hovered ? Color.FromArgb(246, 246, 246) : Color.FromArgb(253, 253, 253);
            Color border = Primary ? fill : Dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(214, 214, 214);
            Color text = Primary ? Dark ? Color.Black : Color.White : Dark ? Color.White : Color.FromArgb(27, 27, 27);
            float radius = 4 * Compat.SystemDpi / 96f;
            using var shape = DetailsForm.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), radius);
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(border);
            e.Graphics.FillPath(brush, shape);
            e.Graphics.DrawPath(pen, shape);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues)
            {
                using var focus = DetailsForm.Rounded(new RectangleF(1.5f, 1.5f, Width - 3, Height - 3), radius);
                using var focusPen = new Pen(Dark ? Color.White : Color.Black, Math.Max(1, Compat.SystemDpi / 96f));
                e.Graphics.DrawPath(focusPen, focus);
            }
        }
    }

    // A check box drawn to match the light or dark dialog (the standard one keeps a light box in dark mode).
    private sealed class DialogCheck : CheckBox
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool Dark { get; set; }
        internal DialogCheck()
        {
            AutoSize = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            CheckedChanged += (_, _) => Invalidate();
            GotFocus += (_, _) => Invalidate();
            LostFocus += (_, _) => Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Compat.SystemDpi / 96f, size = 18 * scale;
            var box = new RectangleF(.5f, (Height - size) / 2, size, size);
            using var shape = DetailsForm.Rounded(box, 4 * scale);
            Color accent = Dark ? Color.FromArgb(76, 194, 255) : Color.FromArgb(0, 103, 192);
            using var fill = new SolidBrush(Checked ? accent : BackColor);
            using var border = new Pen(Checked ? accent : Dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(110, 110, 110), Math.Max(1, scale));
            e.Graphics.FillPath(fill, shape);
            e.Graphics.DrawPath(border, shape);
            if (Checked)
            {
                using var mark = new Pen(Dark ? Color.Black : Color.White, 2 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                e.Graphics.DrawLines(mark, [new PointF(box.X + size * .25f, box.Y + size * .52f), new PointF(box.X + size * .44f, box.Y + size * .70f), new PointF(box.X + size * .76f, box.Y + size * .32f)]);
            }
            var textBounds = new Rectangle((int)Math.Round(28 * scale), 0, Width - (int)Math.Round(28 * scale), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1), ForeColor, BackColor);
        }
    }
}
