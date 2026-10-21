using System.Diagnostics;
using System.Reflection;

// Small stand-ins for what newer .NET provides, so the app builds against .NET Framework 4.8 that ships with Windows.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
    [AttributeUsage(AttributeTargets.Method, Inherited = false)] internal sealed class ModuleInitializerAttribute : Attribute { }
}

namespace UsageLimitViewer
{
    // Loads the library DLLs embedded in the exe (System.Text.Json and its helpers) the first time they are needed.
    internal static class EmbeddedAssemblies
    {
        private static readonly Dictionary<string, Assembly> Loaded = new(StringComparer.OrdinalIgnoreCase);

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Register() => AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            var name = new AssemblyName(e.Name).Name;
            lock (Loaded)
            {
                if (Loaded.TryGetValue(name, out var done)) return done;
                using var stream = typeof(EmbeddedAssemblies).Assembly.GetManifestResourceStream("lib." + name + ".dll");
                if (stream == null) return null;
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return Loaded[name] = Assembly.Load(buffer.ToArray());
            }
        };
    }

    internal static class Compat
    {
        internal static string ProcessPath { get; } = Process.GetCurrentProcess().MainModule!.FileName;
        internal static int ProcessId { get; } = Process.GetCurrentProcess().Id;
        // .NET Framework reports 96 from Control.DeviceDpi unless a config file opts in, so ask Windows directly.
        // The app is system DPI aware, so this one value is right for all of its windows.
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetDpiForSystem();
        internal static int SystemDpi { get; } = (int)GetDpiForSystem();
        internal static long TickCount64 => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

        internal static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        internal static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        internal static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

        // Quotes one command-line argument the way the Windows C runtime parses it.
        internal static string Quote(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0) return argument;
            var text = new System.Text.StringBuilder("\"");
            for (int i = 0; i < argument.Length; i++)
            {
                int slashes = 0;
                while (i < argument.Length && argument[i] == '\\') { slashes++; i++; }
                if (i == argument.Length) { text.Append('\\', slashes * 2); break; }
                if (argument[i] == '"') text.Append('\\', slashes * 2 + 1).Append('"');
                else text.Append('\\', slashes).Append(argument[i]);
            }
            return text.Append('"').ToString();
        }
        internal static void SetArguments(this ProcessStartInfo start, IEnumerable<string> arguments) =>
            start.Arguments = string.Join(" ", arguments.Select(Quote));

        internal static Task WaitForExitAsync(this Process process, CancellationToken token)
        {
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => done.TrySetResult(true);
            if (process.HasExited) done.TrySetResult(true);
            if (token.CanBeCanceled) token.Register(() => done.TrySetCanceled());
            return done.Task;
        }

        // Reads a line, giving up when the token is cancelled (the caller then stops the process, which ends the read).
        internal static async Task<string?> ReadLineAsync(this StreamReader reader, CancellationToken token)
        {
            var read = reader.ReadLineAsync();
            var cancelled = Task.Delay(Timeout.Infinite, token);
            if (await Task.WhenAny(read, cancelled) != read) throw new OperationCanceledException(token);
            return await read;
        }

        // Ends a process and everything it started, like Process.Kill(entireProcessTree: true) on newer .NET.
        internal static void KillTree(this Process process)
        {
            try
            {
                using var search = new System.Management.ManagementObjectSearcher("SELECT ProcessId FROM Win32_Process WHERE ParentProcessId=" + process.Id);
                foreach (var child in search.Get())
                    try { using var p = Process.GetProcessById(Convert.ToInt32(child["ProcessId"])); p.KillTree(); } catch (ArgumentException) { }
            }
            catch (System.Management.ManagementException) { }
            try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        }

        internal static void MoveOver(string source, string destination)
        {
            if (File.Exists(destination)) File.Replace(source, destination, null);
            else File.Move(source, destination);
        }
    }
}

internal static class CompatExtensions
{
    internal static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
    {
        key = pair.Key;
        value = pair.Value;
    }
    internal static bool Contains(this string text, string value, StringComparison comparison) => text.IndexOf(value, comparison) >= 0;
}

// Newer WinForms projects get this from a source generator.
internal static class ApplicationConfiguration
{
    internal static void Initialize()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }
}
