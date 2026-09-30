using System.Runtime.InteropServices;

namespace CodexLimitViewer;

internal static class AppPaths
{
    internal static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));
    internal static string SharedDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexLimitViewer");
    internal static string DataDirectory => IsPortable ? Path.Combine(AppContext.BaseDirectory, "Data") : SharedDataDirectory;
}

internal static class StartupShortcut
{
    internal static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Codex Limit Viewer.lnk");
    internal static bool IsEnabled => File.Exists(PathName);

    internal static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(PathName);
            return;
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
    }
}
