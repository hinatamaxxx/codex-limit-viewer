using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace CodexLimitViewerSetup;

internal sealed class SetupForm : Form
{
    private static readonly Color Surface = Color.FromArgb(32, 32, 36), Card = Color.FromArgb(40, 41, 47);
    private static readonly Color Ink = Color.FromArgb(245, 245, 247), Muted = Color.FromArgb(190, 195, 207);
    private readonly SetupOptions options;
    private readonly int? previewDpi;
    private readonly PictureBox appImage = new() { SizeMode = PictureBoxSizeMode.Zoom, TabStop = false };
    private readonly PictureBox captionIcon = new() { SizeMode = PictureBoxSizeMode.Zoom, TabStop = false };
    private readonly Label caption = NewLabel();
    private readonly ChromeCloseButton captionClose = new();
    private readonly Label brand = NewLabel(), version = NewLabel(), heading = NewLabel(), explanation = NewLabel();
    private readonly Label destinationTitle = NewLabel(), destination = NewLabel(), optionsTitle = NewLabel(), removalHint = NewLabel();
    private readonly Label languageTitle = NewLabel(), status = NewLabel();
    private readonly RoundedCard destinationCard = new(), optionsCard = new();
    private readonly ModernCheckBox startup = new(), launch = new(), removeData = new();
    private readonly LanguageSelector language = new();
    private readonly RoundedButton action = new() { Primary = true }, close = new();
    private readonly ProgressBar progress = new() { Visible = false };
    private readonly string textFamily;
    private float scale = 1;
    private bool running, complete, failed;
    internal int ExitCode { get; private set; }
    private bool English => language.SelectedIndex == 1;
    private string T(string ja, string en) => English ? en : ja;

    internal SetupForm(SetupOptions options, int? previewDpi = null)
    {
        this.options = options;
        this.previewDpi = previewDpi;
        var families = FontFamily.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        textFamily = families.Contains("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        Text = "Codex Limit Viewer";
        AccessibleName = options.Uninstall ? "Codex Limit Viewer Uninstall" : "Codex Limit Viewer Setup";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.None; // Bounds and pixel fonts are scaled together once, including DPI changes.
        BackColor = Surface;
        ForeColor = Ink;
        DoubleBuffered = true;
        Font = UiFont(14);
        ClientSize = new Size(660, 568);
        LoadAppIcon();
        captionIcon.Image = appImage.Image;
        caption.Text = "Codex Limit Viewer";
        caption.ForeColor = Muted;
        captionClose.Click += (_, _) => Close();
        caption.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) DragWindow(); };
        captionIcon.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) DragWindow(); };
        brand.Text = "Codex Limit Viewer";
        version.Text = "Version 0.1.19";
        brand.ForeColor = heading.ForeColor = destination.ForeColor = Ink;
        version.ForeColor = explanation.ForeColor = destinationTitle.ForeColor = optionsTitle.ForeColor = removalHint.ForeColor = languageTitle.ForeColor = status.ForeColor = Muted;
        destinationCard.Controls.AddRange([destinationTitle, destination]);
        optionsCard.Controls.AddRange([optionsTitle, startup, launch, removeData, removalHint]);
        destinationTitle.BackColor = destination.BackColor = optionsTitle.BackColor = removalHint.BackColor = Card;
        foreach (var option in new[] { startup, launch, removeData }) { option.BackColor = Card; option.ForeColor = Ink; }
        language.BackColor = Card;
        language.ForeColor = Ink;
        language.SelectedIndex = options.English ? 1 : 0;
        startup.Checked = options.Startup ?? InstallerService.StartupEnabled;
        launch.Checked = options.Launch;
        removeData.Checked = options.RemoveData;
        startup.Visible = launch.Visible = !options.Uninstall;
        removeData.Visible = removalHint.Visible = options.Uninstall;
        Controls.AddRange([captionIcon, caption, captionClose, appImage, brand, version, heading, explanation, destinationCard, optionsCard, progress, status, languageTitle, language, action, close]);
        startup.TabIndex = removeData.TabIndex = 0;
        launch.TabIndex = 1;
        language.TabIndex = 2;
        action.TabIndex = 3;
        close.TabIndex = 4;
        destinationCard.TabStop = optionsCard.TabStop = false;
        language.SelectedIndexChanged += (_, _) => { UpdateText(); LayoutUi(); };
        removeData.CheckedChanged += (_, _) => UpdateText();
        action.Click += async (_, _) => await RunOperation();
        close.Click += (_, _) => Close();
        FormClosing += (_, e) => { if (running) e.Cancel = true; };
        AcceptButton = action;
        CancelButton = close;
        UpdateText();
        LayoutUi();
    }

    private static Label NewLabel() => new() { AutoSize = false, AutoEllipsis = false, BackColor = Surface, TabStop = false };
    private Font UiFont(float pixels, bool semibold = false) => new(semibold ? "Segoe UI Semibold" : textFamily, pixels * scale, FontStyle.Regular, GraphicsUnit.Pixel);
    private int S(float value) => (int)Math.Round(value * scale);
    private void Place(Control control, int x, int y, int width, int height) => control.SetBounds(S(x), S(y), S(width), S(height));

    private void LoadAppIcon()
    {
        using var stream = typeof(SetupForm).Assembly.GetManifestResourceStream("CodexLimitViewerSetup.App.ico");
        if (stream == null) return;
        using var icon = new Icon(stream, 128, 128);
        Icon = (Icon)icon.Clone();
        appImage.Image = icon.ToBitmap();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int dark = 1, rounded = 2, caption = Surface.R | Surface.G << 8 | Surface.B << 16, captionText = Ink.R | Ink.G << 8 | Ink.B << 16;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(Handle, 33, ref rounded, sizeof(int));
        DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
        DwmSetWindowAttribute(Handle, 36, ref captionText, sizeof(int));
        LayoutUi();
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); LayoutUi(); }
    protected override void OnShown(EventArgs e) { base.OnShown(e); LayoutUi(); action.Focus(); }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr value, IntPtr extra);
    private void DragWindow() { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); }

    protected override void WndProc(ref Message message)
    {
        const int WmNcHitTest = 0x84;
        if (message.Msg == WmNcHitTest)
        {
            long coordinates = message.LParam.ToInt64();
            var point = PointToClient(new Point(unchecked((short)coordinates), unchecked((short)(coordinates >> 16))));
            if (point.Y >= 0 && point.Y < S(40) && !captionClose.Bounds.Contains(point)) { message.Result = (IntPtr)2; return; }
        }
        base.WndProc(ref message);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(Color.FromArgb(70, 74, 86));
        using var frame = Shapes.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), S(8));
        e.Graphics.DrawPath(border, frame);
    }

    private void LayoutUi()
    {
        scale = (previewDpi ?? DeviceDpi) / 96f;
        SuspendLayout();
        try
        {
            Font = UiFont(14);
            caption.Font = UiFont(12);
            foreach (var label in new[] { explanation, destination, status }) label.Font = UiFont(14);
            foreach (var label in new[] { version, destinationTitle, optionsTitle, removalHint, languageTitle }) label.Font = UiFont(13);
            brand.Font = UiFont(25, true);
            heading.Font = UiFont(22, true);
            startup.Font = launch.Font = removeData.Font = UiFont(14);
            action.Font = close.Font = UiFont(14, true);
            language.Font = UiFont(13);
            destination.Text = WrapPath(InstallerService.InstallFolder, destination.Font, S(558));
            int pathLines = destination.Text.Count(c => c == '\n') + 1;
            int cardHeight = Math.Max(98, 54 + pathLines * 22);
            int optionsY = 193 + cardHeight + 16, footerY = optionsY + 130 + 17;
            ClientSize = new Size(S(660), S(footerY + 152));
            Place(captionIcon, 13, 11, 18, 18);
            Place(caption, 40, 8, 568, 24);
            Place(captionClose, 608, 0, 52, 40);
            Place(appImage, 32, 70, 52, 52);
            Place(brand, 100, 69, 528, 34);
            Place(version, 100, 108, 528, 23);
            Place(heading, 32, 157, 596, 33);
            Place(explanation, 32, 196, 596, 25);
            Place(destinationCard, 32, 233, 596, cardHeight);
            Place(destinationTitle, 18, 16, 560, 22);
            Place(destination, 18, 46, 560, cardHeight - 54);
            Place(optionsCard, 32, optionsY + 40, 596, 130);
            Place(optionsTitle, 18, 14, 560, 22);
            Place(startup, 18, 45, 560, 32);
            Place(launch, 18, 86, 560, 32);
            Place(removeData, 18, 45, 560, 32);
            Place(removalHint, 50, 87, 528, 23);
            Place(status, 32, footerY + 40, 596, 43);
            Place(progress, 32, footerY + 86, 596, 3);
            Place(languageTitle, 32, footerY + 110, 68, 24);
            Place(language, 104, footerY + 104, 184, 36);
            Place(action, 360, footerY + 101, 168, 40);
            Place(close, 540, footerY + 101, 88, 40);
            foreach (var control in new Control[] { destinationCard, optionsCard, startup, launch, removeData, action, close }) control.Invalidate();
        }
        finally { ResumeLayout(); }
    }

    private static string WrapPath(string path, Font font, int width)
    {
        var lines = new List<string>();
        string remaining = path;
        while (TextRenderer.MeasureText(remaining, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width > width)
        {
            int fit = remaining.Length - 1;
            while (fit > 1 && TextRenderer.MeasureText(remaining[..fit], font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width > width) fit--;
            int separator = remaining.LastIndexOf(Path.DirectorySeparatorChar, fit - 1, fit);
            int cut = separator > 0 ? separator + 1 : fit;
            lines.Add(remaining[..cut]);
            remaining = remaining[cut..];
        }
        lines.Add(remaining);
        return string.Join(Environment.NewLine, lines);
    }

    private void UpdateText()
    {
        bool updating = File.Exists(InstallerService.AppPath);
        heading.Text = complete ? T("準備ができました", "You're all set") : options.Uninstall ? T("アプリを削除", "Uninstall the app")
            : updating ? T("最新版に更新", "Update your app") : T("インストール", "Install the app");
        explanation.Text = options.Uninstall ? T("アプリと起動用ショートカットを削除します。", "Remove the app and its launch shortcuts.")
            : updating ? T("設定を引き継いで、アプリを置き換えます。", "Replace the existing app and keep your settings.")
            : T("タスクバーから、残りの利用枠をひと目で。", "See your remaining AI limits from the taskbar.");
        destinationTitle.Text = T("インストール先", "Installation folder");
        optionsTitle.Text = options.Uninstall ? T("データの扱い", "Your data") : T("起動の設定", "Launch options");
        startup.Text = T("Windows 起動時に開始する", "Launch at Windows startup");
        launch.Text = T("インストール後に起動する", "Launch after installation");
        removeData.Text = T("設定と取得データも削除する", "Also remove settings and cached data");
        removalHint.Text = removeData.Checked ? T("設定と取得データを削除します。", "Your saved settings and cached data will be removed.") : T("設定と取得データは保持します。", "Your saved settings and cached data will be kept.");
        languageTitle.Text = T("言語", "Language");
        action.Text = complete ? T("完了", "Done") : options.Uninstall ? T("削除", "Uninstall") : updating ? T("更新する", "Update") : T("インストール", "Install");
        close.Text = T("閉じる", "Close");
        if (!running && !complete && !failed) status.Text = T("レジストリは変更しません。", "No changes are made to the registry.");
        foreach (var option in new[] { startup, launch, removeData }) option.AccessibleName = option.Text;
        action.AccessibleName = action.Text;
        close.AccessibleName = close.Text;
        language.AccessibleName = T("表示言語", "Display language");
    }

    internal bool CapturedSurfaceMatches(Bitmap image)
    {
        bool Matches(Point point, Color expected)
        {
            var pixel = image.GetPixel(point.X, point.Y);
            return Math.Abs(pixel.R - expected.R) + Math.Abs(pixel.G - expected.G) + Math.Abs(pixel.B - expected.B) < 12;
        }
        return Matches(new Point(S(16), S(144)), Surface) && Matches(new Point(destinationCard.Right - S(8), destinationCard.Top + destinationCard.Height / 2), Card)
            && Matches(new Point(optionsCard.Right - S(8), optionsCard.Top + optionsCard.Height / 2), Card);
    }

    private async Task RunOperation()
    {
        if (complete) { Close(); return; }
        running = true;
        captionClose.Enabled = action.Enabled = close.Enabled = language.Enabled = startup.Enabled = launch.Enabled = removeData.Enabled = false;
        progress.Visible = true;
        progress.Style = ProgressBarStyle.Marquee;
        status.ForeColor = Muted;
        status.Text = T("処理しています…", "Working…");
        try
        {
            bool startupChoice = startup.Checked, launchChoice = launch.Checked, removeChoice = removeData.Checked;
            await Task.Run(() =>
            {
                if (options.Uninstall) InstallerService.Uninstall(removeChoice);
                else InstallerService.Install(startupChoice, launchChoice);
            });
            complete = true;
            ExitCode = 0;
            status.ForeColor = Ink;
            status.Text = options.Uninstall ? T("この画面を閉じると、削除が完了します。", "Close this window to finish removing the app.")
                : T("インストールが完了しました。スタートメニューから起動できます。", "Installation complete. Launch the app from the Start Menu.");
            UpdateText();
        }
        catch (Exception e)
        {
            failed = true;
            ExitCode = 1;
            status.ForeColor = Color.FromArgb(255, 153, 164);
            status.Text = T("処理できませんでした。もう一度お試しください。", "The operation failed. You can try again.");
            MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            progress.Style = ProgressBarStyle.Blocks;
            progress.Visible = false;
            running = false;
            captionClose.Enabled = action.Enabled = close.Enabled = true;
            language.Enabled = startup.Enabled = launch.Enabled = removeData.Enabled = !complete;
            action.Focus();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { appImage.Image?.Dispose(); Icon?.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class RoundedCard : Panel
{
    internal RoundedCard() { BackColor = Color.FromArgb(32, 32, 36); DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Shapes.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 12 * DeviceDpi / 96f);
        using var fill = new SolidBrush(Color.FromArgb(40, 41, 47));
        using var border = new Pen(Color.FromArgb(60, 63, 73));
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }
}

internal sealed class ModernCheckBox : CheckBox
{
    private bool hovered;
    internal ModernCheckBox()
    {
        AutoSize = false;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        CheckedChanged += (_, _) => Invalidate();
        GotFocus += (_, _) => Invalidate();
        LostFocus += (_, _) => Invalidate();
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = Font.Size / 14f, size = 20 * scale;
        var box = new RectangleF(.5f, (Height - size) / 2, size, size);
        using var shape = Shapes.Rounded(box, 4 * scale);
        using var fill = new SolidBrush(Checked ? Enabled ? Color.FromArgb(96, 205, 255) : Color.FromArgb(75, 86, 101) : hovered ? Color.FromArgb(54, 57, 65) : Color.FromArgb(36, 37, 42));
        using var border = new Pen(Checked ? fill.Color : Color.FromArgb(143, 148, 160), Math.Max(1, scale));
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(border, shape);
        if (Checked)
        {
            using var mark = new Pen(Color.FromArgb(22, 28, 34), 2 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            e.Graphics.DrawLines(mark, [new PointF(box.X + size * .25f, box.Y + size * .51f), new PointF(box.X + size * .44f, box.Y + size * .70f), new PointF(box.X + size * .77f, box.Y + size * .32f)]);
        }
        var textBounds = new Rectangle((int)Math.Round(32 * scale), 0, Width - (int)Math.Round(32 * scale), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : Color.FromArgb(121, 126, 139), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1), ForeColor, BackColor);
    }
}

internal sealed class RoundedButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Primary { get; init; }
    private bool hovered, pressed;
    internal RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
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
        e.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(32, 32, 36));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = Font.Size / 14f;
        Color color = !Enabled ? Color.FromArgb(48, 51, 59) : Primary ? pressed ? Color.FromArgb(76, 172, 214) : hovered ? Color.FromArgb(119, 214, 255) : Color.FromArgb(96, 205, 255)
            : pressed ? Color.FromArgb(41, 43, 50) : hovered ? Color.FromArgb(63, 66, 76) : Color.FromArgb(49, 51, 59);
        using var shape = Shapes.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 8 * scale);
        using var fill = new SolidBrush(color);
        using var border = new Pen(Primary ? color : Color.FromArgb(79, 82, 95));
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(border, shape);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, !Enabled ? Color.FromArgb(121, 126, 139) : Primary ? Color.FromArgb(20, 29, 36) : Color.FromArgb(245, 245, 247), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues)
        {
            using var focus = Shapes.Rounded(new RectangleF(3, 3, Width - 6, Height - 6), 6 * scale);
            using var focusPen = new Pen(Primary ? Color.FromArgb(29, 62, 80) : Color.FromArgb(195, 216, 232));
            e.Graphics.DrawPath(focusPen, focus);
        }
    }
}

internal sealed class LanguageSelector : UserControl
{
    private readonly SegmentRadioButton japanese = new() { Text = "日本語", AccessibleName = "日本語" };
    private readonly SegmentRadioButton english = new() { Text = "English", AccessibleName = "English" };
    internal event EventHandler? SelectedIndexChanged;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int SelectedIndex
    {
        get => english.Checked ? 1 : 0;
        set { if (value == 1) english.Checked = true; else japanese.Checked = true; }
    }
    internal LanguageSelector()
    {
        BackColor = Color.FromArgb(40, 41, 47);
        DoubleBuffered = true;
        Controls.AddRange([japanese, english]);
        japanese.Checked = true;
        japanese.CheckedChanged += (_, _) => { if (japanese.Checked) SelectedIndexChanged?.Invoke(this, EventArgs.Empty); };
        english.CheckedChanged += (_, _) => { if (english.Checked) SelectedIndexChanged?.Invoke(this, EventArgs.Empty); };
    }
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (japanese != null && english != null) japanese.Font = english.Font = Font;
        PerformLayout();
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (japanese == null || english == null) return;
        int padding = Math.Max(2, (int)Math.Round(Font.Size / 13f * 3));
        int half = (Width - padding * 3) / 2;
        japanese.SetBounds(padding, padding, half, Height - padding * 2);
        english.SetBounds(padding * 2 + half, padding, half, Height - padding * 2);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(32, 32, 36));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Shapes.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), Font.Size / 13f * 8);
        using var fill = new SolidBrush(BackColor);
        using var border = new Pen(Color.FromArgb(69, 72, 84));
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(border, shape);
    }
}

internal sealed class SegmentRadioButton : RadioButton
{
    private bool hovered;
    internal SegmentRadioButton()
    {
        AutoSize = false;
        AutoCheck = true;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        CheckedChanged += (_, _) => Invalidate();
        GotFocus += (_, _) => Invalidate();
        LostFocus += (_, _) => Invalidate();
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(40, 41, 47));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Shapes.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), Font.Size / 13f * 5);
        using var fill = new SolidBrush(Checked ? Color.FromArgb(68, 72, 84) : hovered ? Color.FromArgb(52, 55, 64) : Color.FromArgb(40, 41, 47));
        e.Graphics.FillPath(fill, shape);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? Color.FromArgb(245, 245, 247) : Color.FromArgb(121, 126, 139), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2), Color.White, fill.Color);
    }
}

internal sealed class ChromeCloseButton : Button
{
    private bool hovered;
    internal ChromeCloseButton()
    {
        TabStop = false;
        AccessibleName = "Close";
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(hovered && Enabled ? Color.FromArgb(196, 43, 28) : Color.FromArgb(32, 32, 36));
        using var glyph = new Font("Segoe MDL2 Assets", 12 * Height / 40f, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(e.Graphics, "\uE8BB", glyph, ClientRectangle, Enabled ? Color.FromArgb(225, 227, 233) : Color.FromArgb(101, 106, 119), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal static class Shapes
{
    internal static GraphicsPath Rounded(RectangleF rectangle, float radius)
    {
        float diameter = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
