using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace CodexLimitViewerSetup;

// Writes System.AppUserModel.ID into a shortcut file (no registry), matching the ID the app sets for itself at startup.
internal static class AppUserModel
{
    internal const string Id = "hinatamaxxx.CodexLimitViewer";

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink { }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit)] private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

    internal static void Stamp(string shortcut)
    {
        var link = new ShellLink();
        try
        {
            var file = (IPersistFile)link;
            file.Load(shortcut, 2); // STGM_READWRITE
            var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 }; // PKEY_AppUserModel_ID
            var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(Id) }; // VT_LPWSTR
            try
            {
                var store = (IPropertyStore)link;
                store.SetValue(ref key, ref value);
                store.Commit();
            }
            finally { PropVariantClear(ref value); }
            file.Save(shortcut, true);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }
}
