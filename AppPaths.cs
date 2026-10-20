using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CodexLimitViewer;

internal static class AppPaths
{
    // A file named "portable" beside the exe keeps settings and cache in the adjacent Data folder (v1.0 used "portable.flag").
    internal static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable")) ||
        File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));
    private static string PortableData => Path.Combine(AppContext.BaseDirectory, "Data");
    // Cache and the Claude Code bridge stay on this machine; the bridge path is fixed because Claude Code's settings point at it.
    internal static string SharedDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexLimitViewer");
    internal static string DataDirectory => IsPortable ? PortableData : SharedDataDirectory;
    internal static string SettingsDirectory => IsPortable ? PortableData : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexLimitViewer");
}

// Windows lists shortcuts in the Startup folder under Settings → Apps → Startup and records the user's choice itself.
internal static class StartupShortcut
{
    internal static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Codex Limit Viewer.lnk");
    internal static bool Exists => File.Exists(PathName);
    internal static bool IsEnabled => Exists && IsApprovedByWindows(ReadApproval());

    // Windows writes 12 bytes per shortcut; an odd first byte means it was turned off in Windows. No value means on.
    internal static bool IsApprovedByWindows(byte[]? value) => value == null || value.Length == 0 || (value[0] & 1) == 0;

    private static byte[]? ReadApproval()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder");
            return key?.GetValue(Path.GetFileName(PathName)) as byte[];
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return null; }
    }

    // Returns false when the shortcut exists but Windows keeps it turned off; only the user can change that in Settings.
    internal static bool SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(PathName);
            return true;
        }
        string exe = Environment.ProcessPath ?? throw new IOException("Application path is unavailable.");
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows shortcuts are unavailable.");
        object shell = Activator.CreateInstance(type)!;
        object? shortcut = null;
        try
        {
            dynamic host = shell;
            shortcut = host.CreateShortcut(PathName);
            dynamic link = shortcut;
            link.TargetPath = exe;
            link.Arguments = "";
            link.WorkingDirectory = Path.GetDirectoryName(exe)!;
            link.Description = "Codex Limit Viewer";
            link.IconLocation = exe + ",0";
            link.Save();
        }
        finally
        {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
        return IsApprovedByWindows(ReadApproval());
    }

    internal static void OpenWindowsSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true })?.Dispose(); } catch { }
    }
}
