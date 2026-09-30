using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace CodexLimitViewerSetup;

internal static class InstallerService
{
    internal static string InstallFolder => Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CodexLimitViewer"));
    internal static string AppPath => Path.Combine(InstallFolder, "CodexLimitViewer.exe");
    private static string SetupPath => Path.Combine(InstallFolder, "Setup.exe");
    private static string DataFolder => Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexLimitViewer"));
    private static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Codex Limit Viewer.lnk");
    private static string UninstallLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Uninstall Codex Limit Viewer.lnk");
    private static string StartupLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Codex Limit Viewer.lnk");
    internal static bool StartupEnabled => File.Exists(StartupLink);

    internal static bool IsInsideInstallFolder(string path) => Path.GetFullPath(path).StartsWith(InstallFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal static void Install(bool startup, bool launch)
    {
        var parent = Directory.GetParent(InstallFolder)!.FullName;
        Directory.CreateDirectory(parent);
        var identifier = Guid.NewGuid().ToString("N");
        var stage = Path.Combine(parent, ".CodexLimitViewer-stage-" + identifier);
        var backup = Path.Combine(parent, ".CodexLimitViewer-backup-" + identifier);
        bool hadExisting = Directory.Exists(InstallFolder), oldMoved = false, newMoved = false, committed = false;
        var links = new[] { StartMenuLink, UninstallLink, StartupLink }.ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllBytes(p) : null);
        try
        {
            Directory.CreateDirectory(stage);
            ExtractPayload(stage);
            if (!File.Exists(Path.Combine(stage, "CodexLimitViewer.exe"))) throw new IOException("The installer payload is missing CodexLimitViewer.exe.");
            File.WriteAllText(Path.Combine(stage, ".installation-id"), identifier);
            // A full directory replacement prevents old versions and portable markers from surviving an update.
            File.Delete(Path.Combine(stage, "portable.flag"));
            File.Copy(Environment.ProcessPath ?? throw new IOException("The setup executable path is unavailable."), Path.Combine(stage, "Setup.exe"), true);
            StopUserApps();
            PreserveLegacyPortableData();
            if (hadExisting) { Directory.Move(InstallFolder, backup); oldMoved = true; }
            Directory.Move(stage, InstallFolder); newMoved = true;
            Shortcuts.Create(StartMenuLink, AppPath, InstallFolder);
            Shortcuts.Create(UninstallLink, SetupPath, InstallFolder, "--uninstall");
            if (startup) Shortcuts.Create(StartupLink, AppPath, InstallFolder);
            else if (File.Exists(StartupLink)) File.Delete(StartupLink);
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

    internal static void Uninstall(bool removeData)
    {
        StopUserApps();
        Shortcuts.RemoveIfTarget(StartMenuLink, AppPath);
        Shortcuts.RemoveIfTarget(UninstallLink, SetupPath);
        Shortcuts.RemoveIfTarget(StartupLink, AppPath);
        if (IsInsideInstallFolder(Environment.ProcessPath!))
        {
            var marker = Path.Combine(InstallFolder, ".installation-id");
            if (!File.Exists(marker)) File.WriteAllText(marker, Guid.NewGuid().ToString("N"));
            var installationId = File.ReadAllText(marker).Trim();
            if (!Guid.TryParseExact(installationId, "N", out _)) throw new IOException("The installation identifier is invalid.");
            StartRemovalHelper(InstallFolder, removeData ? DataFolder : null, null, installationId);
            return;
        }
        DeleteInstallFolder();
        if (removeData) DeleteDataFolder();
    }

    private static void ExtractPayload(string stage)
    {
        using var stream = typeof(InstallerService).Assembly.GetManifestResourceStream("CodexLimitViewerSetup.Payload.zip")
            ?? throw new IOException("The installer payload is unavailable.");
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
        if (!File.Exists(Path.Combine(InstallFolder, "portable.flag")) || !Directory.Exists(legacy)) return;
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

    private static void DeleteDataFolder()
    {
        var target = Path.GetFullPath(DataFolder);
        var expected = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexLimitViewer"));
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
        try { StartRemovalHelper(null, null, folder); } catch { }
    }

    // PowerShell remains in one shell and removes only exact, revalidated paths after this executable exits.
    private static void StartRemovalHelper(string? install, string? data, string? temporarySetup, string? expectedInstallationId = null)
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
            if (data != null)
                body += "    $taskExpectedData = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CodexLimitViewer'))\r\n" +
                    "    $taskData = [IO.Path]::GetFullPath(" + Literal(data) + ")\r\n" +
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
