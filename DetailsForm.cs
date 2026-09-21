using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexLimitViewer;

internal sealed class DetailsForm : Form
{
    private readonly Preferences prefs;
    private string? rowsKey;
    private readonly System.Windows.Forms.Timer clock = new() { Interval = 1000 };
    private readonly Button pin = new(), refresh = new(), hide = new();
    private readonly BufferedPanel list = new() { AutoScroll = true, BackColor = Color.FromArgb(32, 32, 36) };
    private Reading codex = new("Codex", [], null), agy = new("Antigravity", [], null);
    private bool expanded, refreshing, moving;
    private Point dragOrigin, windowOrigin;
    private Size target;
    internal event Action? RefreshRequested;
    internal Func<Rectangle?>? TrayBounds;
    internal void PositionAtTray()
    {
        if (TrayBounds == null) return;
        var bounds = TrayBounds();
        var area = bounds is Rectangle r ? Screen.FromRectangle(r).WorkingArea : Screen.PrimaryScreen!.WorkingArea;
        var anchor = bounds ?? new Rectangle(area.Right - 100, area.Bottom, 24, 24);
        Location = TrayPresentation.Above(anchor, Size, area);
    }
    private static readonly Color Ink = Color.FromArgb(32, 32, 36), Muted = Color.FromArgb(190, 195, 207);
    private static readonly Color Mint = Color.White, Violet = Color.White;
    internal DetailsForm(Preferences preferences)
    {
        prefs = preferences;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        Text = "Codex Limit Viewer";
        AccessibleName = L.T("CodexとAntigravityの残り使用量");
        BackColor = Ink;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 12 * DeviceDpi / 96f, FontStyle.Regular, GraphicsUnit.Pixel);
        DoubleBuffered = true;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(344, 54);
        target = ClientSize;
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Right - Width - 16, area.Bottom - Height - 8);
        ClampPosition();
        MakeButton(pin, L.T("固定"), L.T("常時表示を切り替える"), () => SetPinned(!prefs.Pinned));
        MakeButton(refresh, "↻", L.T("CodexとAntigravityを更新"), () => RefreshRequested?.Invoke());
        MakeButton(hide, "×", L.T("通知領域に収納"), Hide);
        Controls.Add(list);
        clock.Tick += (_, _) =>
        {
            if (!Visible) return;
            PositionAtTray();
            if (expanded)
            {
                RebuildRows();
                foreach (var row in list.Controls.OfType<QuotaRow>()) row.RefreshCountdown();
            }
        };
        clock.Start();
        Deactivate += (_, _) => { if (!prefs.Pinned) Hide(); };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { if (expanded && prefs.Pinned) Expand(false); else Hide(); }
            if (e.KeyCode is Keys.Enter or Keys.Space) Expand(!expanded);
        };
        Resize += (_, _) => { RoundWindow(); LayoutButtons(); PositionAtTray(); Invalidate(); };
        RoundWindow(); LayoutButtons();
    }
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string theme, string? subId);
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        SetWindowTheme(list.Handle, "DarkMode_Explorer", null);

    }
    private void MakeButton(Button b, string text, string accessible, Action action)
    {
        b.Text = text; b.AccessibleName = accessible; b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0; b.BackColor = Ink; b.ForeColor = Muted;
        b.Cursor = Cursors.Hand; b.Click += (_, _) => action(); Controls.Add(b);
    }
    private void LayoutButtons()
    {
        pin.Visible = refresh.Visible = hide.Visible = expanded;
        list.Visible = expanded;
        pin.SetBounds(Width - 156, 17, 62, 30);
        refresh.SetBounds(Width - 91, 17, 30, 30);
        hide.SetBounds(Width - 55, 17, 30, 30);
        pin.ForeColor = prefs.Pinned ? Mint : Muted;
        list.SetBounds(20, 85, Math.Max(20, Width - 40), Math.Max(1, Height - 125));
    }
    private void RoundWindow()
    {
        using var path = Rounded(new RectangleF(0, 0, Width, Height), 10);
        var old = Region; Region = new Region(path); old?.Dispose();
    }
    internal void SetPinned(bool value)
    {
        prefs.Pinned = value; prefs.Save(); LayoutButtons(); Invalidate();
    }
    internal void UpdateReadings(Reading c, Reading a, bool busy)
    {
        codex = c; agy = a; refreshing = busy;
        if (expanded) RebuildRows();
        Invalidate();
    }
    internal void Reveal(bool nearTray = false)
    {
        PositionAtTray();
        Expand(true, false); PositionAtTray(); Show(); Activate();
    }
    internal void Expand(bool value, bool animate = true)
    {
        expanded = value;
        int expandedHeight = Math.Min(Screen.FromRectangle(Bounds).WorkingArea.Height - 24,
            Math.Clamp(330 + 70 * (codex.Quotas.Count + agy.Quotas.Count), 482, 720));
        target = value ? new Size(460, expandedHeight) : new Size(344, 54);
        if (value) RebuildRows();
        ClientSize = target; ClampPosition();
        LayoutButtons(); Invalidate();
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        dragOrigin = Cursor.Position; windowOrigin = Location; moving = false; Capture = true;
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            if (expanded && prefs.Pinned) Expand(false); else Hide();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Capture || e.Button != MouseButtons.Left) return;
        var delta = new Size(Cursor.Position.X - dragOrigin.X, Cursor.Position.Y - dragOrigin.Y);
        if (Math.Abs(delta.Width) + Math.Abs(delta.Height) > 5) moving = true;
        if (moving && TrayBounds == null) Location = windowOrigin + delta;
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); Capture = false;
        if (e.Button != MouseButtons.Left) return;
        if (!moving && !expanded) Expand(true);
        else { ClampPosition(); prefs.X = Left; prefs.Y = Top; prefs.Save(); }
    }
    private void ClampPosition()
    {
        var a = Screen.FromRectangle(Bounds).WorkingArea;
        Location = new(Math.Clamp(Left, a.Left, Math.Max(a.Left, a.Right - Width)), Math.Clamp(Top, a.Top, Math.Max(a.Top, a.Bottom - Height)));
    }
    private void RebuildRows()
    {
        // Feed polling and refresh status can repeat unchanged readings.
        var key = System.Text.Json.JsonSerializer.Serialize(new { codex, agy, cStale = codex.Stale, aStale = agy.Stale });
        if (key == rowsKey) return;
        rowsKey = key;
        var scroll = list.AutoScrollPosition;
        list.SuspendLayout();
        foreach (Control c in list.Controls.Cast<Control>().ToArray()) { list.Controls.Remove(c); c.Dispose(); }
        int y = 0;
        foreach (var reading in new[] { codex, agy })
        {
            var color = reading.Provider == "Codex" ? Mint : Violet;
            AddLabel(reading.Provider, new Rectangle(8, y, 230, 25), color, 12);
            string status = reading.Updated == null ? L.T("未接続") : reading.Stale ? L.T("前回の値") : reading.Provider == "Codex" ? L.T("直接取得") : L.T("CLIで取得");
            AddLabel(status, new Rectangle(232, y, 160, 28), Muted, 11);
            y += 36;
            if (reading.Quotas.Count == 0)
            {
                AddLabel(reading.Error ?? L.T("接続しています…"), new Rectangle(8, y, 364, 42), Muted, 10);
                y += 48;
            }
            foreach (var quota in reading.Quotas)
            {
                var row = new QuotaRow(quota, reading.Stale, color) { Location = new(4, y), Size = new(372, 65) };
                list.Controls.Add(row); y += 70;
            }
            if (reading.Quotas.Count > 0)
            {
                var age = reading.Updated?.ToLocalTime().ToString("MM/dd HH:mm:ss") ?? "—";
                AddLabel(L.F("取得 {age}", ("age", age)) + (reading.Error == null ? "" : L.T(" · 更新できません")), new Rectangle(8, y, 370, 28), Muted, 12);
                y += 32;
            }
            y += 20;
        }
        list.AutoScrollMinSize = new Size(0, y);
        list.ResumeLayout(); list.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
    }
    private void AddLabel(string text, Rectangle bounds, Color color, float size, FontStyle style = FontStyle.Regular)
    {
        list.Controls.Add(new Label { Text = L.T(text), Bounds = bounds, ForeColor = color, Font = new Font("Segoe UI", size * DeviceDpi / 96f, style, GraphicsUnit.Pixel), AutoEllipsis = true });
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(Color.FromArgb(48, 53, 64));
        using var p = Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 9); g.DrawPath(border, p);
        if (!expanded)
        {
            DrawDot(g, 19, 23, Mint); DrawText(g, "Codex", 35, 16, 10, Muted);
            DrawText(g, codex.Compact + (codex.Stale && codex.Quotas.Count > 0 ? "*" : ""), 100, 15, 12, Color.White);
            using var pen = new Pen(Color.FromArgb(57, 60, 71)); g.DrawLine(pen, 156, 17, 156, 37);
            DrawDot(g, 174, 23, Violet); DrawText(g, "AGY", 190, 16, 10, Muted);
            DrawText(g, agy.Compact + (agy.Stale && agy.Quotas.Count > 0 ? "*" : ""), 236, 16, 11, Color.White);
            DrawText(g, "⌄", 315, 16, 10, Muted);
        }
        else
        {
            DrawText(g, L.T("使用状況"), 28, 20, 12, Color.White);
            using var divider = new Pen(Color.FromArgb(58, 58, 63)); g.DrawLine(divider, 28, 66, Width - 28, 66);
            DrawText(g, refreshing ? L.T("使用状況を更新中…") : L.T("Esc で閉じる"), 28, Height - 30, 11, Muted);
        }
    }
    internal static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath(); float d = radius * 2;
        p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
    private static void DrawDot(Graphics g, int x, int y, Color color) { using var b = new SolidBrush(color); g.FillEllipse(b, x, y, 7, 7); }
    internal static void DrawText(Graphics g, string text, int x, int y, float size, Color color)
    {
        using var f = new Font("Segoe UI", size * g.DpiY / 96f, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, text, f, new Point(x, y), color, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { clock.Dispose(); }
        base.Dispose(disposing);
    }
    private sealed class BufferedPanel : Panel
    {
        internal BufferedPanel() { DoubleBuffered = true; }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x02000000; return p; }
        }
    }
    internal static string FormatCountdown(TimeSpan? remain)
    {
        if (remain == null) return L.T("リセット時刻不明");
        if (remain <= TimeSpan.Zero) return L.T("リセット時刻経過 · 更新待ち");
        var t = remain.Value;
        return t.Days > 0
            ? L.F("リセットまで {t.Days}日 {t.Hours}時間 {t.Minutes}分", ("t.Days", t.Days), ("t.Hours", t.Hours), ("t.Minutes", t.Minutes))
            : L.F("リセットまで {t.Hours}時間 {t.Minutes}分", ("t.Hours", t.Hours), ("t.Minutes", t.Minutes));
    }
    private sealed class QuotaRow : Control
    {
        private readonly Quota quota;
        private readonly bool stale;
        private readonly Color accent;
        private string countdown;
        internal QuotaRow(Quota quota, bool stale, Color accent)
        {
            this.quota = quota; this.stale = stale; this.accent = accent;
            DoubleBuffered = true;
            countdown = FormatCountdown(quota.Reset - DateTimeOffset.UtcNow);
        }
        internal void RefreshCountdown()
        {
            var next = FormatCountdown(quota.Reset - DateTimeOffset.UtcNow);
            if (next == countdown) return;
            countdown = next;
            Invalidate(new Rectangle(0, 28, Width, Height - 28));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var label = L.QuotaLabel(quota.Label);
            DrawText(g, label, 4, 1, 12, Color.White);
            using var valueFont = new Font("Segoe UI", 12 * DeviceDpi / 96f, FontStyle.Regular, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, $"{quota.Remaining:0.#}%", valueFont, new Rectangle(284, 0, Width - 284, 26), stale ? Muted : accent, TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            DrawText(g, countdown, 4, 30, 11, Muted);
        }
    }
}
