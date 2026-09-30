using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodexLimitViewerSetup;

internal sealed record SetupOptions(bool Silent, bool Uninstall, bool Launch, bool? Startup, bool RemoveData, bool English)
{
    internal static SetupOptions Parse(string[] args)
    {
        bool Has(string value) => args.Contains(value, StringComparer.OrdinalIgnoreCase);
        if (Has("--startup") && Has("--no-startup")) throw new ArgumentException("Choose --startup or --no-startup.");
        return new(Has("--silent"), Has("--uninstall"), !Has("--no-launch"),
            Has("--startup") ? true : Has("--no-startup") ? false : null, Has("--remove-data"), Has("--english"));
    }
}

internal static class Program
{
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [STAThread]
    private static int Main(string[] args)
    {
        SetupOptions options;
        try { options = SetupOptions.Parse(args); }
        catch (Exception e) { MessageBox.Show(e.Message, "Codex Limit Viewer Setup"); return 1; }

        int previewArgument = Array.FindIndex(args, a => a.Equals("--render-preview", StringComparison.OrdinalIgnoreCase));
        int captureArgument = Array.FindIndex(args, a => a.Equals("--capture-preview", StringComparison.OrdinalIgnoreCase));
        bool captureScreen = captureArgument >= 0;
        if (captureScreen) previewArgument = captureArgument;
        if (previewArgument >= 0)
        {
            try
            {
                if (previewArgument + 1 >= args.Length) throw new ArgumentException("--render-preview requires an output PNG path.");
                int? previewDpi = null;
                int dpiArgument = Array.FindIndex(args, a => a.Equals("--preview-dpi", StringComparison.OrdinalIgnoreCase));
                if (dpiArgument >= 0)
                {
                    if (dpiArgument + 1 >= args.Length || !int.TryParse(args[dpiArgument + 1], out int value) || value is < 96 or > 288)
                        throw new ArgumentException("--preview-dpi requires a value from 96 to 288.");
                    previewDpi = value;
                }
                ApplicationConfiguration.Initialize();
                using var form = new SetupForm(options, previewDpi, args.Contains("--public-preview", StringComparer.OrdinalIgnoreCase));
                var output = Path.GetFullPath(args[previewArgument + 1]);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                Exception? previewError = null;
                using var captureTimer = new System.Windows.Forms.Timer { Interval = 250 };
                form.Shown += (_, _) => { form.Activate(); form.BringToFront(); captureTimer.Start(); };
                captureTimer.Tick += (_, _) =>
                {
                    captureTimer.Stop();
                    try
                    {
                        form.Update();
                        DwmFlush();
                        using var bitmap = new Bitmap(form.Width, form.Height);
                        if (captureScreen)
                        {
                            using var graphics = Graphics.FromImage(bitmap);
                            graphics.CopyFromScreen(form.Location, Point.Empty, form.Size);
                            if (!form.CapturedSurfaceMatches(bitmap)) throw new IOException("The captured pixels do not show the setup window.");
                        }
                        else form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                        bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    catch (Exception e) { previewError = e; }
                    finally { form.Close(); }
                };
                Application.Run(form);
                if (previewError != null) throw previewError;
                return 0;
            }
            catch (Exception e) { Report(e, options); return 1; }
        }

        // A running executable cannot replace its own installed file. Continue from a disposable copy instead.
        if (!options.Uninstall && InstallerService.IsInsideInstallFolder(Environment.ProcessPath!))
        {
            if (options.Silent)
            {
                Report(new IOException("For silent installation or update, run the downloaded setup executable outside the installation folder."), options);
                return 3;
            }
            try
            {
                var temporary = Path.Combine(Path.GetTempPath(), "CodexLimitViewerSetup-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporary);
                var copy = Path.Combine(temporary, "CodexLimitViewerSetup.exe");
                File.Copy(Environment.ProcessPath!, copy);
                var start = new ProcessStartInfo(copy) { UseShellExecute = true, WorkingDirectory = temporary };
                foreach (var arg in args) start.ArgumentList.Add(arg);
                _ = Process.Start(start) ?? throw new IOException("Unable to start the setup copy.");
                return 0;
            }
            catch (Exception e) { Report(e, options); return 1; }
        }

        using var mutex = new Mutex(false, "Local\\CodexLimitViewerSetup");
        bool owns;
        try { owns = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { owns = true; }
        if (!owns)
        {
            if (!options.Silent) MessageBox.Show(options.English ? "Another setup is already running." : "ほかのセットアップが実行中です。", "Codex Limit Viewer Setup");
            return 2;
        }
        try
        {
            if (options.Silent)
            {
                if (options.Uninstall) InstallerService.Uninstall(options.RemoveData);
                else InstallerService.Install(options.Startup ?? InstallerService.StartupEnabled, options.Launch);
                return 0;
            }
            ApplicationConfiguration.Initialize();
            using var form = new SetupForm(options);
            Application.Run(form);
            return form.ExitCode;
        }
        catch (Exception e) { Report(e, options); return 1; }
        finally
        {
            mutex.ReleaseMutex();
            InstallerService.ScheduleTemporarySetupCleanup();
        }
    }

    private static void Report(Exception e, SetupOptions options)
    {
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "CodexLimitViewer-Setup.log"), e.ToString()); } catch { }
        if (!options.Silent) MessageBox.Show(e.Message, "Codex Limit Viewer Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
