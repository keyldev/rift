using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Rift.Shell;

/// <summary>Windows' own Properties and "Open with" dialogs.</summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class ShellDialogs
{
    /// <summary>The Properties sheet: one item, or the combined sheet for several. Returns immediately.</summary>
    public static void Properties(nint owner, IReadOnlyList<string> paths)
    {
        if (paths.Count == 1)
        {
            SHObjectProperties(owner, SHOP_FILEPATH, paths[0], null);
            return;
        }

        using var items = ShellItems.FromPaths(paths);
        if (items is null)
            return;
        var dataObject = items.GetUIObject(owner, IID_IDataObject);
        try
        {
            SHMultiFileProperties(dataObject, 0);
        }
        finally
        {
            Marshal.Release(dataObject);
        }
    }

    /// <summary>"Open with": pick an app, optionally make it the default for this type, then run it.</summary>
    public static void OpenWith(nint owner, string path)
    {
        fixed (char* file = path)
        {
            var info = new OPENASINFO { pcszFile = file, oaifInFlags = OAIF_ALLOW_REGISTRATION | OAIF_REGISTER_EXT | OAIF_EXEC };
            SHOpenWithDialog(owner, &info);
        }
    }

    private static readonly Guid IID_IDataObject = new("0000010E-0000-0000-C000-000000000046");
    private const uint SHOP_FILEPATH = 2;
    private const uint OAIF_ALLOW_REGISTRATION = 1;
    private const uint OAIF_REGISTER_EXT = 2;
    private const uint OAIF_EXEC = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct OPENASINFO
    {
        public char* pcszFile;
        public char* pcszClass;
        public uint oaifInFlags;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SHObjectProperties(nint hwnd, uint type, string name, string? page);

    [LibraryImport("shell32.dll")]
    private static partial int SHMultiFileProperties(nint dataObject, uint flags);

    [LibraryImport("shell32.dll")]
    private static partial int SHOpenWithDialog(nint hwnd, OPENASINFO* info);
}
