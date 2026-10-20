using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CodexLimitViewer;

// A Windows 11 style confirmation: text on top, a tinted button band below, Cancel focused first.
internal sealed class ExitDialog : Form
{
    private readonly bool dark = IsDarkTheme();
    private readonly Label heading = new(), body = new();
    private readonly DialogButton exit = new() { Primary = true, DialogResult = DialogResult.OK };
    private readonly DialogButton cancel = new() { DialogResult = DialogResult.Cancel };
    private readonly Panel band = new();
    private Color Surface => dark ? Color.FromArgb(39, 39, 39) : Color.White;
    private Color Band => dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
    private Color Ink => dark ? Color.White : Color.FromArgb(27, 27, 27);
    private Color Muted => dark ? Color.FromArgb(206, 206, 206) : Color.FromArgb(68, 68, 68);

    internal ExitDialog()
    {
        Text = "Codex Limit Viewer";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None; // Laid out from DeviceDpi, including after a DPI change.
        TopMost = true;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }
        BackColor = Surface;
        heading.Text = L.T("Codex Limit Viewer を終了しますか？");
        body.Text = L.T("終了すると、タスクバーの残量表示と定期的な更新が止まります。") + "\n\n" +
            L.T("もう一度使うときは、スタートメニューの「Codex Limit Viewer」から起動してください。自動起動がオンなら、次にサインインしたときにも起動します。");
        heading.ForeColor = Ink; body.ForeColor = Muted;
        heading.BackColor = body.BackColor = Surface;
        exit.Text = L.T("終了する");
        cancel.Text = L.T("キャンセル");
        band.BackColor = Band;
        exit.Dark = cancel.Dark = dark;
        band.Controls.AddRange([exit, cancel]);
        Controls.AddRange([heading, body, band]);
        exit.TabIndex = 1; cancel.TabIndex = 0;
        AcceptButton = exit;
        CancelButton = cancel;
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
        float scale = DeviceDpi / 96f;
        int S(float value) => (int)Math.Round(value * scale);
        string family = FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        heading.Font = new Font("Segoe UI Semibold", 20 * scale, GraphicsUnit.Pixel);
        body.Font = new Font(family, 14 * scale, GraphicsUnit.Pixel);
        exit.Font = cancel.Font = new Font(family, 14 * scale, GraphicsUnit.Pixel);
        int width = S(460), inner = width - S(56);
        var bodySize = TextRenderer.MeasureText(body.Text, body.Font, new Size(inner, int.MaxValue), TextFormatFlags.WordBreak);
        heading.SetBounds(S(28), S(26), inner, S(30));
        body.SetBounds(S(28), S(26 + 30 + 12), inner, bodySize.Height + S(4));
        int bandTop = body.Bottom + S(28), bandHeight = S(18 + 32 + 18);
        band.SetBounds(0, bandTop, width, bandHeight);
        cancel.SetBounds(width - S(28) - S(112), S(18), S(112), S(32));
        exit.SetBounds(cancel.Left - S(8) - S(112), S(18), S(112), S(32));
        ClientSize = new Size(width, bandTop + bandHeight);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int value = dark ? 1 : 0;
        DwmSetWindowAttribute(Handle, 20, ref value, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); LayoutDialog(); }
    protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); cancel.Focus(); }
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
            e.Graphics.Clear(Parent?.BackColor ?? BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // The accent color is Windows' default blue for both actions: exiting is not dangerous, so it is never red.
            Color fill = Primary
                ? Dark ? pressed ? Color.FromArgb(66, 158, 205) : hovered ? Color.FromArgb(89, 183, 232) : Color.FromArgb(76, 194, 255)
                       : pressed ? Color.FromArgb(0, 84, 153) : hovered ? Color.FromArgb(0, 95, 170) : Color.FromArgb(0, 103, 192)
                : Dark ? pressed ? Color.FromArgb(39, 39, 39) : hovered ? Color.FromArgb(61, 61, 61) : Color.FromArgb(55, 55, 55)
                       : pressed ? Color.FromArgb(240, 240, 240) : hovered ? Color.FromArgb(246, 246, 246) : Color.FromArgb(253, 253, 253);
            Color border = Primary ? fill : Dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(214, 214, 214);
            Color text = Primary ? Dark ? Color.Black : Color.White : Dark ? Color.White : Color.FromArgb(27, 27, 27);
            float radius = 4 * DeviceDpi / 96f;
            using var shape = DetailsForm.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), radius);
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(border);
            e.Graphics.FillPath(brush, shape);
            e.Graphics.DrawPath(pen, shape);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues)
            {
                using var focus = DetailsForm.Rounded(new RectangleF(1.5f, 1.5f, Width - 3, Height - 3), radius);
                using var focusPen = new Pen(Dark ? Color.White : Color.Black, Math.Max(1, DeviceDpi / 96f));
                e.Graphics.DrawPath(focusPen, focus);
            }
        }
    }
}
