using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace CodexLimitViewerSetup;

internal static class InstallerService
{
    internal static string InstallFolder => Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CodexLimitViewer"));
    internal static string AppPath => Path.Combine(InstallFolder, "CodexLimitViewer.exe");
    // v1.0/v1.1 kept a second copy of the setup here; the app exe now uninstalls itself.
    private static string SetupPath => Path.Combine(InstallFolder, "Setup.exe");
    // Cache and the Claude Code bridge stay on this machine; settings roam with the user profile.
    private static string DataFolder => Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexLimitViewer"));
    private static string SettingsFolder => Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexLimitViewer"));
    private static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Codex Limit Viewer.lnk");
    // v1.0 also placed an uninstall shortcut; uninstalling now happens from Settings → Apps → Installed apps.
    private static string LegacyUninstallLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Uninstall Codex Limit Viewer.lnk");
    private static string StartupLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Codex Limit Viewer.lnk");
    private static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Codex Limit Viewer.lnk");
    internal static bool StartupEnabled => File.Exists(StartupLink);
    internal static bool DesktopEnabled => File.Exists(DesktopLink);

    internal static bool IsInsideInstallFolder(string path) => Path.GetFullPath(path).StartsWith(InstallFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal static void Install(bool startup, bool desktop, bool launch)
    {
        var parent = Directory.GetParent(InstallFolder)!.FullName;
        Directory.CreateDirectory(parent);
        var identifier = Guid.NewGuid().ToString("N");
        var stage = Path.Combine(parent, ".CodexLimitViewer-stage-" + identifier);
        var backup = Path.Combine(parent, ".CodexLimitViewer-backup-" + identifier);
        bool hadExisting = Directory.Exists(InstallFolder), oldMoved = false, newMoved = false, committed = false;
        var links = new[] { StartMenuLink, LegacyUninstallLink, StartupLink, DesktopLink }.ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllBytes(p) : null);
        var registration = UninstallEntry.Read();
        try
        {
            Directory.CreateDirectory(stage);
            ExtractPayload(stage);
            // The setup is the app itself: install this very executable.
            File.Copy(Environment.ProcessPath ?? throw new IOException("The setup executable path is unavailable."), Path.Combine(stage, "CodexLimitViewer.exe"), true);
            File.WriteAllText(Path.Combine(stage, ".installation-id"), identifier);
            // A full directory replacement prevents old versions and portable markers from surviving an update.
            File.Delete(Path.Combine(stage, "portable.flag"));
            File.Delete(Path.Combine(stage, "portable"));
            StopUserApps();
            PreserveLegacyPortableData();
            if (hadExisting) { Directory.Move(InstallFolder, backup); oldMoved = true; }
            Directory.Move(stage, InstallFolder); newMoved = true;
            Shortcuts.Create(StartMenuLink, AppPath, InstallFolder);
            Shortcuts.RemoveIfTarget(LegacyUninstallLink, SetupPath);
            if (startup) Shortcuts.Create(StartupLink, AppPath, InstallFolder);
            else if (File.Exists(StartupLink)) File.Delete(StartupLink);
            if (desktop) Shortcuts.Create(DesktopLink, AppPath, InstallFolder);
            else Shortcuts.RemoveIfTarget(DesktopLink, AppPath);
            UninstallEntry.Write(AppPath, AppPath, InstallFolder, typeof(InstallerService).Assembly.GetName().Version!.ToString(3), FolderSizeKilobytes(InstallFolder));
            committed = true;
        }
        catch
        {
            if (newMoved) DeleteInstallFolder();
            if (oldMoved) Directory.Move(backup, InstallFolder);
            foreach (var (path, contents) in links)
            {
                if (contents == null) { if (File.Exists(path)) File.Delete(path); }
                else { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, contents); }
            }
            UninstallEntry.Restore(registration);
            throw;
        }
        finally
        {
            DeleteWorkingFolder(stage, ".CodexLimitViewer-stage-");
            if (committed) DeleteWorkingFolder(backup, ".CodexLimitViewer-backup-");
        }
        if (launch)
            _ = Process.Start(new ProcessStartInfo(AppPath) { UseShellExecute = true, WorkingDirectory = InstallFolder, WindowStyle = ProcessWindowStyle.Hidden })
                ?? throw new IOException("The app was installed, but could not be started.");
    }

    // keepData: leave settings and cache for a later reinstall. By default everything the app created is removed,
    // except the Claude Code bridge script that Claude Code's own settings may still point at.
    internal static void Uninstall(bool keepData)
    {
        bool removeData = !keepData;
        StopUserApps();
        Shortcuts.RemoveIfTarget(StartMenuLink, AppPath);
        Shortcuts.RemoveIfTarget(LegacyUninstallLink, SetupPath);
        Shortcuts.RemoveIfTarget(StartupLink, AppPath);
        Shortcuts.RemoveIfTarget(DesktopLink, AppPath);
        UninstallEntry.Remove();
        if (IsInsideInstallFolder(Environment.ProcessPath!))
        {
            var marker = Path.Combine(InstallFolder, ".installation-id");
            if (!File.Exists(marker)) File.WriteAllText(marker, Guid.NewGuid().ToString("N"));
            var installationId = File.ReadAllText(marker).Trim();
            if (!Guid.TryParseExact(installationId, "N", out _)) throw new IOException("The installation identifier is invalid.");
            StartRemovalHelper(InstallFolder, removeData, null, installationId);
            return;
        }
        DeleteInstallFolder();
        if (removeData) { DeleteDataFolder(DataFolder, "LocalApplicationData"); DeleteDataFolder(SettingsFolder, "ApplicationData"); }
    }

    private static int FolderSizeKilobytes(string folder) =>
        (int)Math.Min(int.MaxValue, Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024);

    private static void ExtractPayload(string stage)
    {
        // Documents and helper scripts installed beside the app; development builds have none.
        using var stream = typeof(InstallerService).Assembly.GetManifestResourceStream("CodexLimitViewerSetup.Payload.zip");
        if (stream == null) return;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var output = Path.GetFullPath(Path.Combine(stage, relative));
            if (!output.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The installer payload contains an invalid path.");
            if (entry.Name.Length == 0) { Directory.CreateDirectory(output); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            entry.ExtractToFile(output, overwrite: false);
        }
    }

    private static void PreserveLegacyPortableData()
    {
        var legacy = Path.Combine(InstallFolder, "Data");
        bool portable = File.Exists(Path.Combine(InstallFolder, "portable")) || File.Exists(Path.Combine(InstallFolder, "portable.flag"));
        if (!portable || !Directory.Exists(legacy)) return;
        // A portable copy may have been put into the fixed installation location by hand. Preserve its full data
        // before replacing that directory, and use only missing files as installed defaults.
        var archive = Path.Combine(DataFolder, "ImportedPortable", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        CopyDataDirectory(legacy, archive, missingOnly: false);
        CopyDataDirectory(legacy, DataFolder, missingOnly: true);
    }

    private static void CopyDataDirectory(string source, string destination, bool missingOnly)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Portable data contains a linked directory. Move that data out of the installation folder before updating.");
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Portable data contains a linked file or directory. Move that data out of the installation folder before updating.");
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0) CopyDataDirectory(entry, target, missingOnly);
            else if (!missingOnly || !File.Exists(target))
            {
                File.Copy(entry, target, overwrite: false);
                File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(entry));
            }
        }
    }

    private static void StopUserApps()
    {
        var currentSid = WindowsIdentity.GetCurrent().User?.Value;
        if (currentSid == null) throw new IOException("Unable to determine the current Windows user.");
        foreach (var process in Process.GetProcessesByName("CodexLimitViewer"))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue; // Uninstalling runs from the app's own exe.
                try
                {
                    var owner = OwnerSid(process.Id);
                    if (owner == null && process.SessionId == Process.GetCurrentProcess().SessionId)
                        throw new IOException("Please close Codex Limit Viewer before continuing.");
                    if (owner != currentSid) continue;
                    process.Kill(entireProcessTree: true);
                    if (!process.WaitForExit(10000)) throw new IOException("Please close Codex Limit Viewer before continuing.");
                }
                catch (InvalidOperationException) { } // The app already exited.
                catch (System.ComponentModel.Win32Exception e) { throw new IOException("Please close Codex Limit Viewer before continuing.", e); }
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    private static string? OwnerSid(int id)
    {
        var process = OpenProcess(0x1000, false, id); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(process, 0x0008, out var token)) return null; // TOKEN_QUERY
            try { using var identity = new WindowsIdentity(token); return identity.User?.Value; }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    private static void DeleteInstallFolder()
    {
        var target = Path.GetFullPath(InstallFolder);
        var expected = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CodexLimitViewer"));
        if (!target.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected installation path.");
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }

    private static void DeleteDataFolder(string folder, string knownFolder)
    {
        var target = Path.GetFullPath(folder);
        var expected = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Enum.Parse<Environment.SpecialFolder>(knownFolder)), "CodexLimitViewer"));
        if (!target.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected data path.");
        if (!Directory.Exists(target)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(target))
        {
            if (Path.GetFileName(entry).Equals("ClaudeCodeStatusLine.ps1", StringComparison.OrdinalIgnoreCase)) continue;
            var full = Path.GetFullPath(entry);
            if (!full.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected data file path.");
            if ((File.GetAttributes(full) & FileAttributes.Directory) != 0) Directory.Delete(full, true);
            else File.Delete(full);
        }
        if (!Directory.EnumerateFileSystemEntries(target).Any()) Directory.Delete(target);
    }

    private static void DeleteWorkingFolder(string path, string prefix)
    {
        var full = Path.GetFullPath(path);
        var parent = Directory.GetParent(InstallFolder)!.FullName;
        if (!Path.GetDirectoryName(full)!.Equals(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith(prefix, StringComparison.Ordinal))
            throw new IOException("Unexpected setup working path.");
        try { if (Directory.Exists(full)) Directory.Delete(full, true); }
        catch (IOException) { } // The valid installation remains usable if antivirus temporarily holds an old file.
        catch (UnauthorizedAccessException) { }
    }

    internal static void ScheduleTemporarySetupCleanup()
    {
        var folder = Path.GetDirectoryName(Environment.ProcessPath!);
        if (folder == null || !Path.GetDirectoryName(folder)!.Equals(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return;
        if (!Path.GetFileName(folder).StartsWith("CodexLimitViewerSetup-", StringComparison.Ordinal)) return;
        try { StartRemovalHelper(null, false, folder); } catch { }
    }

    // PowerShell remains in one shell and removes only exact, revalidated paths after this executable exits.
    private static void StartRemovalHelper(string? install, bool removeData, string? temporarySetup, string? expectedInstallationId = null)
    {
        static string Literal(string value) => "'" + value.Replace("'", "''") + "'";
        var script = Path.Combine(Path.GetTempPath(), "CodexLimitViewer-Remove-" + Guid.NewGuid().ToString("N") + ".ps1");
        var body = "$ErrorActionPreference = 'Stop'\r\n" +
            "$taskProcess = Get-Process -Id " + Environment.ProcessId + " -ErrorAction SilentlyContinue\r\n" +
            "if ($taskProcess) { $taskProcess.WaitForExit() }\r\n";
        if (install != null)
        {
            if (expectedInstallationId == null) throw new IOException("The uninstall operation needs an installation identifier.");
            body += @"$taskMutex = [Threading.Mutex]::new($false, 'Local\CodexLimitViewerSetup')" + "\r\n" +
                "$taskOwnsMutex = $false\r\ntry {\r\n" +
                "  try { $taskOwnsMutex = $taskMutex.WaitOne() } catch [Threading.AbandonedMutexException] { $taskOwnsMutex = $true }\r\n" +
                "  $taskExpected = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/CodexLimitViewer'))\r\n" +
                "  $taskTarget = [IO.Path]::GetFullPath(" + Literal(install) + ")\r\n" +
                "  if ($taskTarget -ne $taskExpected) { throw 'Unexpected installation path' }\r\n" +
                "  $taskMarker = Join-Path $taskTarget '.installation-id'\r\n" +
                "  if ([IO.File]::Exists($taskMarker) -and [IO.File]::ReadAllText($taskMarker).Trim() -eq " + Literal(expectedInstallationId) + ") {\r\n" +
                "    if (Test-Path -LiteralPath $taskTarget) { Remove-Item -LiteralPath $taskTarget -Recurse -Force }\r\n";
            if (removeData)
                foreach (var (folder, known) in new[] { (DataFolder, "LocalApplicationData"), (SettingsFolder, "ApplicationData") })
                    body += "    $taskExpectedData = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('" + known + "')) 'CodexLimitViewer'))\r\n" +
                        "    $taskData = [IO.Path]::GetFullPath(" + Literal(folder) + ")\r\n" +
                        "    if ($taskData -ne $taskExpectedData) { throw 'Unexpected data path' }\r\n" +
                        "    if (Test-Path -LiteralPath $taskData) {\r\n" +
                        "      foreach ($taskEntry in Get-ChildItem -LiteralPath $taskData -Force) {\r\n" +
                        "        if ($taskEntry.Name -eq 'ClaudeCodeStatusLine.ps1') { continue }\r\n" +
                        "        $taskEntryPath = [IO.Path]::GetFullPath($taskEntry.FullName)\r\n" +
                        "        if (-not $taskEntryPath.StartsWith($taskData + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected data file path' }\r\n" +
                        "        Remove-Item -LiteralPath $taskEntryPath -Recurse -Force\r\n" +
                        "      }\r\n" +
                        "      if (-not (Get-ChildItem -LiteralPath $taskData -Force)) { Remove-Item -LiteralPath $taskData -Force }\r\n" +
                        "    }\r\n";
            body += "  }\r\n} finally {\r\n  if ($taskOwnsMutex) { $taskMutex.ReleaseMutex() }\r\n  $taskMutex.Dispose()\r\n}\r\n";
        }
        if (temporarySetup != null)
            body += "$taskTarget = [IO.Path]::GetFullPath(" + Literal(temporarySetup) + ")\r\n" +
                "$taskParent = [IO.Path]::GetDirectoryName($taskTarget)\r\n" +
                "if ($taskParent -ne [IO.Path]::GetTempPath().TrimEnd([IO.Path]::DirectorySeparatorChar) -or [IO.Path]::GetFileName($taskTarget) -notlike 'CodexLimitViewerSetup-*') { throw 'Unexpected temporary setup path' }\r\n" +
                "if (Test-Path -LiteralPath $taskTarget) { Remove-Item -LiteralPath $taskTarget -Recurse -Force }\r\n";
        body += "Remove-Item -LiteralPath $PSCommandPath -Force\r\n";
        File.WriteAllText(script, body, new System.Text.UTF8Encoding(true));
        var helper = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) helper.ArgumentList.Add(arg);
        _ = Process.Start(helper) ?? throw new IOException("Unable to start the uninstall cleanup.");
    }
}

// The one registry entry the setup writes: it lists the app under Settings → Apps → Installed apps.
internal static class UninstallEntry
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLimitViewer";

    internal static Dictionary<string, (object Value, RegistryValueKind Kind)>? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValueNames().ToDictionary(n => n, n => (key.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames)!, key.GetValueKind(n)));
    }

    internal static void Write(string app, string setup, string folder, string version, int sizeKilobytes)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue("DisplayName", "Codex Limit Viewer");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "hinatamaxxx");
        key.SetValue("DisplayIcon", app + ",0");
        key.SetValue("InstallLocation", folder);
        key.SetValue("UninstallString", "\"" + setup + "\" --uninstall");
        key.SetValue("QuietUninstallString", "\"" + setup + "\" --uninstall --silent");
        key.SetValue("URLInfoAbout", "https://github.com/hinatamaxxx/codex-limit-viewer");
        key.SetValue("EstimatedSize", sizeKilobytes, RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    internal static void Restore(Dictionary<string, (object Value, RegistryValueKind Kind)>? previous)
    {
        try
        {
            Remove();
            if (previous == null) return;
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            foreach (var (name, (value, kind)) in previous) key.SetValue(name, value, kind);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    internal static void Remove() => Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
}

internal static class Shortcuts
{
    internal static void Create(string link, string target, string workingDirectory, string arguments = "")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows shortcut support is unavailable.");
        dynamic shell = Activator.CreateInstance(type)!;
        object? shortcutObject = null;
        try
        {
            dynamic shortcut = shell.CreateShortcut(link);
            shortcutObject = shortcut;
            shortcut.TargetPath = target;
            shortcut.Arguments = arguments;
            shortcut.WorkingDirectory = workingDirectory;
            shortcut.Description = "Codex Limit Viewer";
            shortcut.IconLocation = target + ",0";
            shortcut.Save();
        }
        finally
        {
            if (shortcutObject != null) Marshal.FinalReleaseComObject(shortcutObject);
            Marshal.FinalReleaseComObject(shell);
        }
        AppUserModel.Stamp(link);
    }

    internal static void RemoveIfTarget(string link, string target)
    {
        if (!File.Exists(link)) return;
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows shortcut support is unavailable.");
        dynamic shell = Activator.CreateInstance(type)!;
        object? shortcutObject = null;
        try
        {
            dynamic shortcut = shell.CreateShortcut(link);
            shortcutObject = shortcut;
            string actual = shortcut.TargetPath;
            if (!string.IsNullOrWhiteSpace(actual) && Path.GetFullPath(actual).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Delete(link);
        }
        finally
        {
            if (shortcutObject != null) Marshal.FinalReleaseComObject(shortcutObject);
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
