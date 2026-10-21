using UsageLimitViewer;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UsageLimitViewerSetup;

// Uninstalling removes settings and cache by default; --keep-settings keeps them.
internal sealed record SetupOptions(bool Silent, bool Uninstall, bool Launch, bool? Startup, bool? Desktop, bool KeepData, bool English)
{
    internal static SetupOptions Parse(string[] args)
    {
        bool Has(string value) => args.Contains(value, StringComparer.OrdinalIgnoreCase);
        if (Has("--startup") && Has("--no-startup")) throw new ArgumentException("Choose --startup or --no-startup.");
        if (Has("--desktop") && Has("--no-desktop")) throw new ArgumentException("Choose --desktop or --no-desktop.");
        return new(Has("--silent"), Has("--uninstall"), !Has("--no-launch"),
            Has("--startup") ? true : Has("--no-startup") ? false : null,
            Has("--desktop") ? true : Has("--no-desktop") ? false : null, Has("--keep-settings"), Has("--english"));
    }
}

// The exe is both the app and its setup: --setup installs it, --uninstall removes it.
internal static class SetupProgram
{
    internal static bool Requested(string[] args) =>
        args.Any(a => a.Equals("--setup", StringComparison.OrdinalIgnoreCase) || a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));

    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    internal static int Run(string[] args)
    {
        args = args.Where(a => !a.Equals("--setup", StringComparison.OrdinalIgnoreCase)).ToArray();
        SetupOptions options;
        try { options = SetupOptions.Parse(args); }
        catch (Exception e) { MessageBox.Show(e.Message, "Usage Limit Viewer Setup"); return 1; }

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

        // A running executable cannot replace or delete its own installed file. Continue from a disposable copy instead,
        // so an uninstall can show its progress and the finished screen while the install folder goes away.
        // A silent uninstall from Installed apps still runs here and leaves the folder to a helper after it exits.
        if (InstallerService.IsInsideInstallFolder(UsageLimitViewer.Compat.ProcessPath!) && !(options.Uninstall && options.Silent))
        {
            if (options.Silent)
            {
                Report(new IOException("For silent installation or update, run the downloaded setup executable outside the installation folder."), options);
                return 3;
            }
            try
            {
                var temporary = Path.Combine(Path.GetTempPath(), "UsageLimitViewerSetup-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporary);
                var copy = Path.Combine(temporary, "UsageLimitViewerSetup.exe");
                File.Copy(UsageLimitViewer.Compat.ProcessPath!, copy);
                var start = new ProcessStartInfo(copy) { UseShellExecute = true, WorkingDirectory = temporary };
                start.SetArguments(options.Uninstall ? args : args.Prepend("--setup"));
                _ = Process.Start(start) ?? throw new IOException("Unable to start the setup copy.");
                return 0;
            }
            catch (Exception e) { Report(e, options); return 1; }
        }

        using var mutex = new Mutex(false, "Local\\UsageLimitViewerSetup");
        bool owns;
        try { owns = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { owns = true; }
        if (!owns)
        {
            if (!options.Silent) MessageBox.Show(options.English ? "Another setup is already running." : "ほかのセットアップが実行中です。", "Usage Limit Viewer Setup");
            return 2;
        }
        try
        {
            if (options.Silent)
            {
                if (options.Uninstall) InstallerService.Uninstall(options.KeepData);
                else InstallerService.Install(options.Startup ?? InstallerService.DefaultStartup, options.Desktop ?? InstallerService.DefaultDesktop, options.Launch);
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
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "UsageLimitViewer-Setup.log"), e.ToString()); } catch { }
        if (!options.Silent) MessageBox.Show(e.Message, "Usage Limit Viewer Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
