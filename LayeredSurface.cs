using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CodexLimitViewer;

internal static class LayeredSurface
{
    [StructLayout(LayoutKind.Sequential)] private struct Pair { public int X, Y; public Pair(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Operation, Flags, Alpha, Format; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination,
        ref Pair location, ref Pair size, IntPtr source, ref Pair origin, uint key, ref Blend blend, uint flags);

    internal static void Present(IntPtr window, Bitmap bitmap, Point location)
    {
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var image = bitmap.GetHbitmap(Color.FromArgb(0));
        var previous = SelectObject(memory, image);
        try
        {
            var position = new Pair(location.X, location.Y);
            var size = new Pair(bitmap.Width, bitmap.Height);
            var origin = new Pair(0, 0);
            var blend = new Blend { Alpha = 255, Format = 1 };
            if (!UpdateLayeredWindow(window, screen, ref position, ref size, memory, ref origin, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            SelectObject(memory, previous); DeleteObject(image); DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
