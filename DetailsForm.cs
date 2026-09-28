using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexLimitViewer;

internal sealed class DetailsForm : Form
{
    private readonly Preferences prefs;
    private string? rowsKey;
    private int contentHeight;
    private readonly System.Windows.Forms.Timer clock = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer fade = new() { Interval = 15 };
    private const int FadeDurationMs = 160;
    private long fadeStarted;
    private double fadeFrom, fadeTo;
    private bool openedFromHover;
    private readonly Button pin = new(), refresh = new(), hide = new();
    private readonly BufferedPanel list = new() { AutoScroll = true, BackColor = Color.FromArgb(32, 32, 36) };
    // Folded services stay pinned under the scrolling list so they are always in view.
    private readonly BufferedPanel footer = new() { BackColor = Color.FromArgb(32, 32, 36) };
    private int footerHeight;
    private string? revealProvider;
    // Folded services the reader opened; cleared each time the popup opens so they start folded again.
    private readonly HashSet<string> openedProviders = [];
    private Reading codex = new("Codex", [], null), agy = new("Antigravity", [], null);
    private Reading? claude;
    private Reading? grok;
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
        MakeButton(refresh, "↻", L.T("使用状況を更新"), () => RefreshRequested?.Invoke());
        MakeButton(hide, "×", L.T("通知領域に収納"), CloseDetails);
        Controls.Add(list);
        Controls.Add(footer);
        clock.Tick += (_, _) =>
        {
            if (!Visible) return;
            PositionAtTray();
            if (expanded)
            {
                RebuildRows();
                foreach (var row in list.Controls.OfType<ProviderCard>().SelectMany(c => c.Controls.OfType<QuotaRow>())) row.RefreshCountdown();
            }
        };
        clock.Start();
        fade.Tick += (_, _) => AdvanceFade();
        Deactivate += (_, _) => { if (!prefs.Pinned) CloseDetails(); };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { if (expanded && prefs.Pinned) Expand(false); else CloseDetails(); }
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
        footer.Visible = expanded && footerHeight > 0;
        pin.SetBounds(Width - 156, 17, 62, 30);
        refresh.SetBounds(Width - 91, 17, 30, 30);
        hide.SetBounds(Width - 55, 17, 30, 30);
        pin.ForeColor = prefs.Pinned ? Mint : Muted;
        int footerSpace = footerHeight > 0 ? footerHeight + 10 : 0;
        list.SetBounds(20, 85, Math.Max(20, Width - 40), Math.Max(1, Height - 90 - footerSpace));
        footer.SetBounds(20, Height - 5 - footerHeight, Math.Max(20, Width - 40), Math.Max(1, footerHeight));
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
    internal void UpdateReadings(Reading c, Reading a, Reading? cl, Reading? gr, bool busy)
    {
        codex = c; agy = a; claude = cl; grok = gr; refreshing = busy;
        if (expanded) Expand(true, false);
        Invalidate();
    }
    internal void Reveal(bool nearTray = false, bool fromHover = false)
    {
        openedFromHover = fromHover;
        if (!Visible) openedProviders.Clear();
        PositionAtTray();
        Expand(true, false); PositionAtTray();
        // A fresh open starts at the top; re-revealing a popup that is still fading out keeps its scroll.
        if (!Visible) list.AutoScrollPosition = Point.Empty;
        if (!Visible) { Opacity = 0; Show(); }
        Activate();
        FadeTo(1);
    }
    internal bool IsDismissing => fade.Enabled && fadeTo == 0;
    internal void CloseDetails()
    {
        if (openedFromHover) Dismiss(); else HideImmediately();
    }
    internal void HideImmediately()
    {
        fade.Stop();
        if (Visible) Hide();
        Opacity = 1;
    }
    internal void Dismiss()
    {
        if (Visible && !IsDismissing) FadeTo(0);
    }
    private void FadeTo(double targetOpacity)
    {
        if (fade.Enabled && fadeTo == targetOpacity) return;
        fadeFrom = Opacity;
        fadeTo = targetOpacity;
        fadeStarted = Environment.TickCount64;
        fade.Start();
    }
    private void AdvanceFade()
    {
        double progress = Math.Clamp((Environment.TickCount64 - fadeStarted) / (double)FadeDurationMs, 0, 1);
        double eased = progress * progress * (3 - 2 * progress);
        Opacity = fadeFrom + (fadeTo - fadeFrom) * eased;
        if (progress < 1) return;
        fade.Stop();
        if (fadeTo == 0) { Hide(); Opacity = 1; }
    }
    internal void Expand(bool value, bool animate = true)
    {
        expanded = value;
        if (value) RebuildRows();
        // Fit the popup to what the cards show (folded cards sit in the fixed footer).
        int expandedHeight = Math.Min(Screen.FromRectangle(Bounds).WorkingArea.Height - 24, Math.Clamp(92 + contentHeight + (footerHeight > 0 ? footerHeight + 10 : 0), 220, 720));
        target = value ? new Size(460, expandedHeight) : new Size(344, 54);
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
            if (expanded && prefs.Pinned) Expand(false); else CloseDetails();
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
        var key = System.Text.Json.JsonSerializer.Serialize(new { codex, agy, claude, grok, localDate = DateTime.Today, cStale = codex.Stale, aStale = agy.Stale, clStale = claude?.Stale, grStale = grok?.Stale, top = prefs.TaskbarTop, bottom = prefs.TaskbarBottom, open = string.Join(",", openedProviders), order = ClockTextRenderer.FiveHourFirst });
        if (key == rowsKey) return;
        rowsKey = key;
        var scroll = list.AutoScrollPosition;
        // Child locations are relative to the current scroll offset; lay out from the top or each rebuild adds blank space below.
        list.AutoScrollPosition = Point.Empty;
        list.SuspendLayout();
        foreach (Control c in list.Controls.Cast<Control>().Concat(footer.Controls.Cast<Control>()).ToArray()) { c.Parent!.Controls.Remove(c); c.Dispose(); }
        int y = 0, fy = 0, revealY = -1;
        // Each service gets its own framed card so its quotas read as one group.
        foreach (var reading in OrderedReadings(prefs, codex, agy, claude, grok))
        {
            // Services not shown in the taskbar can be folded to a one-line summary by clicking their header.
            bool foldable = reading.Provider != prefs.TaskbarTop && reading.Provider != prefs.TaskbarBottom;
            bool folded = foldable && !openedProviders.Contains(reading.Provider);
            var card = new ProviderCard { Location = new Point(0, folded ? fy : y), Width = 378 };
            (folded ? footer : list).Controls.Add(card);
            if (reading.Provider == revealProvider && !folded) revealY = y;
            var color = reading.Provider == "Codex" ? Mint : Violet;
            int cy = 10;
            var title = AddLabel(card, (foldable ? (folded ? "▸ " : "▾ ") : "") + reading.Provider, new Rectangle(14, cy, 170, 25), color, 12, FontStyle.Bold);
            string status = folded ? ClockTextRenderer.TaskbarValue(reading)
                : reading.Updated == null ? L.T("未接続") : reading.Stale ? L.T("前回の値") : reading.Source ?? L.T("取得元不明");
            var statusLabel = AddLabel(card, status, new Rectangle(186, cy + 2, 178, 28), folded && !reading.Stale ? Color.White : Muted, folded ? 12 : 11, align: ContentAlignment.TopRight);
            if (foldable)
            {
                var provider = reading.Provider;
                void Toggle(object? _, EventArgs __)
                {
                    if (openedProviders.Remove(provider)) revealProvider = null;
                    else { openedProviders.Add(provider); revealProvider = provider; }
                    // Rebuilding disposes the clicked label, so do it after this click handler returns.
                    BeginInvoke(() => { Expand(true, false); PositionAtTray(); });
                }
                foreach (Control c in new Control[] { card, title, statusLabel }) { c.Cursor = Cursors.Hand; c.Click += Toggle; }
            }
            if (folded)
            {
                card.Height = cy + 36;
                fy += card.Height + 8;
                continue;
            }
            cy += 36;
            if (reading.Quotas.Count == 0)
            {
                AddLabel(card, reading.Error ?? L.T("接続しています…"), new Rectangle(14, cy, 350, 42), Muted, 10);
                cy += 48;
            }
            foreach (var quota in reading.Quotas)
            {
                var row = new QuotaRow(quota, reading.Stale, color) { Location = new(10, cy), Size = new Size(356, 65) };
                card.Controls.Add(row); cy += row.Height + 5;
            }
            if (reading.Quotas.Count > 0)
            {
                var age = reading.Updated?.ToLocalTime().ToString("MM/dd HH:mm:ss") ?? "—";
                AddLabel(card, L.F("取得 {age}", ("age", age)) + (reading.Error == null ? "" : L.T(" · 更新できません")), new Rectangle(10, cy, 354, 26), Muted, 12);
                cy += 30;
            }
            card.Height = cy + 6;
            y += card.Height + 14;
        }
        contentHeight = Math.Max(0, y - 14);
        footerHeight = Math.Max(0, fy - 8);
        list.AutoScrollMinSize = new Size(0, contentHeight);
        list.ResumeLayout();
        // A card just opened from the footer scrolls into view; otherwise keep the reader's position.
        list.AutoScrollPosition = revealY >= 0 ? new Point(0, revealY) : new Point(-scroll.X, -scroll.Y);
        revealProvider = null;
    }
    // Providers shown in the taskbar come first (top row, then bottom row); the rest keep their default order.
    internal static IEnumerable<Reading> OrderedReadings(Preferences display, params Reading?[] readings) =>
        readings.Where(r => r != null).Cast<Reading>()
            .OrderBy(r => r.Provider == display.TaskbarTop ? 0 : r.Provider == display.TaskbarBottom ? 1 : 2);
    private Label AddLabel(Control parent, string text, Rectangle bounds, Color color, float size, FontStyle style = FontStyle.Regular, bool ellipsis = true, ContentAlignment align = ContentAlignment.TopLeft)
    {
        var label = new Label { Text = L.T(text), Bounds = bounds, ForeColor = color, Font = new Font("Segoe UI", size * DeviceDpi / 96f, style, GraphicsUnit.Pixel), AutoEllipsis = ellipsis, TextAlign = align };
        parent.Controls.Add(label);
        return label;
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
            if (refreshing) DrawText(g, L.T("更新中…"), 130, 20, 10, Muted);
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
        if (disposing) { clock.Dispose(); fade.Dispose(); }
        base.Dispose(disposing);
    }
    private sealed class ProviderCard : Panel
    {
        internal ProviderCard() { DoubleBuffered = true; BackColor = Color.FromArgb(40, 41, 47); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var border = new Pen(Color.FromArgb(70, 74, 86));
            using var path = Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8);
            e.Graphics.DrawPath(border, path);
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Clip the square panel background to the rounded border.
            using var path = Rounded(new RectangleF(0, 0, Width, Height), 8);
            Region?.Dispose(); Region = new Region(path); Invalidate();
        }
    }
    private sealed class BufferedPanel : Panel
    {
        internal BufferedPanel() { DoubleBuffered = true; }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x02000000; return p; }
        }
    }
    // One line per quota: the local reset date with weekday first, then the time left, e.g. "10月3日(土) 18:00 にリセット（あと6日3時間）".
    internal static string FormatReset(DateTimeOffset? reset, DateTimeOffset now)
    {
        if (reset == null) return L.T("リセット時刻不明");
        var left = reset.Value - now;
        if (left <= TimeSpan.Zero) return L.T("リセット時刻経過 · 更新待ち");
        // Services report resets like 17:59:59.9; round to the minute so it reads 18:00 as the Claude app does.
        var at = new DateTimeOffset((reset.Value.UtcTicks + TimeSpan.TicksPerMinute / 2) / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute, TimeSpan.Zero).ToLocalTime();
        var today = now.ToLocalTime().Date;
        var ja = System.Globalization.CultureInfo.GetCultureInfo("ja-JP");
        var en = System.Globalization.CultureInfo.InvariantCulture;
        string day = at.Date == today ? L.T("今日")
            : at.Date == today.AddDays(1) ? L.T("明日")
            : L.English ? at.ToString(at.Year == today.Year ? "ddd, MMM d" : "ddd, MMM d, yyyy", en)
            : at.ToString(at.Year == today.Year ? "M月d日(ddd)" : "yyyy年M月d日(ddd)", ja);
        return L.F("{day} {time} にリセット（あと{left}）", ("day", day), ("time", at.ToString("H:mm", en)), ("left", FormatRemaining(at > now ? at - now : left)));
    }
    internal static string FormatRemaining(TimeSpan t) =>
        t.Days > 0 ? L.F("{d}日{h}時間", ("d", t.Days), ("h", t.Hours))
        : t.Hours > 0 ? L.F("{h}時間{m}分", ("h", t.Hours), ("m", t.Minutes))
        : L.F("{m}分", ("m", Math.Max(1, t.Minutes)));
    private sealed class QuotaRow : Control
    {
        private readonly Quota quota;
        private readonly bool stale;
        private readonly Color accent;
        private string resetText;
        internal QuotaRow(Quota quota, bool stale, Color accent)
        {
            this.quota = quota; this.stale = stale; this.accent = accent;
            DoubleBuffered = true;
            resetText = FormatReset(quota.Reset, DateTimeOffset.Now);
        }
        internal void RefreshCountdown()
        {
            var next = FormatReset(quota.Reset, DateTimeOffset.Now);
            if (next == resetText) return;
            resetText = next;
            Invalidate(new Rectangle(0, 28, Width, Height - 28));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var label = L.QuotaLabel(quota.Label);
            DrawText(g, label, 4, 1, 12, Color.White);
            using var valueFont = new Font("Segoe UI", 12 * DeviceDpi / 96f, FontStyle.Regular, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, $"{quota.Remaining:0.#}%", valueFont, new Rectangle(284, 0, Width - 284, 26), stale ? Muted : accent, TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            DrawText(g, resetText, 4, 30, 11, Muted);
        }
    }
}
