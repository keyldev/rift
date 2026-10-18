using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace Rift.Shell;

/// <summary>A 32-bit BGRA image with straight (not premultiplied) alpha.</summary>
public sealed record IconPixels(int Width, int Height, byte[] Bgra);

/// <summary>
/// File icons as Explorer shows them, from the system image list. Many files share one icon,
/// so callers cache by <see cref="KeyFor"/> and most rows never reach the shell.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class ShellIcons
{
    // Files whose icon lives inside the file itself, or that carry an overlay (shortcut arrow).
    private static readonly HashSet<string> OwnIcon = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".ico", ".cur", ".ani", ".url", ".scr", ".cpl", ".msc", ".appref-ms", ".website",
    };

    /// <summary>
    /// The cache key for an item: the extension when every file of that type looks the same,
    /// the full path when the icon is per file (applications, shortcuts, folders with desktop.ini, drives).
    /// </summary>
    public static string KeyFor(string path, bool isDirectory, FileAttributes attributes)
    {
        if (isDirectory)
        {
            // Only read-only/system folders can have a custom icon (desktop.ini); drive roots have their own.
            var custom = (attributes & (FileAttributes.ReadOnly | FileAttributes.System)) != 0 ||
                         string.Equals(Path.GetPathRoot(path), path, StringComparison.OrdinalIgnoreCase);
            return custom ? path : "<folder>";
        }
        var ext = Path.GetExtension(path);
        return ext.Length == 0 ? "<file>" : OwnIcon.Contains(ext) ? path : ext.ToLowerInvariant();
    }

    /// <summary>Loads an icon. Call from an STA thread; it may touch the disk.</summary>
    /// <param name="large">32 px (scaled for the system DPI) instead of 16 px.</param>
    public static IconPixels? Load(string path, bool isDirectory, FileAttributes attributes, bool large = false)
    {
        var perFile = KeyFor(path, isDirectory, attributes) == path;
        var info = default(SHFILEINFOW);
        var flags = SHGFI_SYSICONINDEX | (perFile ? SHGFI_OVERLAYINDEX : SHGFI_USEFILEATTRIBUTES);
        // With USEFILEATTRIBUTES the shell only looks at the name and these attributes, not at the disk.
        var attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        var name = perFile ? path : isDirectory ? "folder" : "file" + Path.GetExtension(path);
        if (SHGetFileInfoW(name, attrs, &info, (uint)sizeof(SHFILEINFOW), flags) == 0)
            return null;

        var index = info.iIcon & 0x00FFFFFF;
        var overlay = (info.iIcon >> 24) & 0xFF;
        if (GetImageList(large) is not { } list)
            return null;

        // INDEXTOOVERLAYMASK: the shortcut arrow and friends are drawn into the icon.
        var drawFlags = ILD_TRANSPARENT | (uint)(overlay << 8);
        if (list.GetIcon(index, drawFlags, out var icon) != Com.S_OK || icon == 0)
            return null;
        try
        {
            return ToPixels(icon);
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    [ThreadStatic] private static IImageList? _small;
    [ThreadStatic] private static IImageList? _large;

    private static IImageList? GetImageList(bool large)
    {
        var list = large ? _large : _small;
        if (list is not null)
            return list;

        var iid = typeof(IImageList).GUID;
        if (SHGetImageList(large ? SHIL_LARGE : SHIL_SMALL, iid, out var ptr) != Com.S_OK || ptr == 0)
            return null;
        list = Com.Wrap<IImageList>(ptr);
        if (large)
            _large = list;
        else
            _small = list;
        return list;
    }

    private static IconPixels? ToPixels(nint icon)
    {
        if (!GetIconInfo(icon, out var info))
            return null;
        try
        {
            if (info.hbmColor == 0)
                return null; // monochrome icon: not worth supporting
            BITMAP bm;
            if (GetObjectW(info.hbmColor, sizeof(BITMAP), &bm) == 0)
                return null;

            int w = bm.bmWidth, h = bm.bmHeight;
            var pixels = new byte[w * h * 4];
            if (!ReadBits(info.hbmColor, w, h, pixels))
                return null;

            // Old icons have no alpha channel: take transparency from the AND mask instead.
            var hasAlpha = false;
            for (var i = 3; i < pixels.Length && !hasAlpha; i += 4)
                hasAlpha = pixels[i] != 0;
            if (!hasAlpha)
            {
                var mask = new byte[w * h * 4];
                var haveMask = info.hbmMask != 0 && ReadBits(info.hbmMask, w, h, mask);
                for (var i = 0; i < pixels.Length; i += 4)
                    pixels[i + 3] = !haveMask || mask[i] == 0 ? (byte)255 : (byte)0;
            }
            return new IconPixels(w, h, pixels);
        }
        finally
        {
            if (info.hbmColor != 0) DeleteObject(info.hbmColor);
            if (info.hbmMask != 0) DeleteObject(info.hbmMask);
        }
    }

    private static bool ReadBits(nint bitmap, int width, int height, byte[] target)
    {
        var header = new BITMAPINFO
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = width,
            biHeight = -height, // top-down rows
            biPlanes = 1,
            biBitCount = 32,
        };
        var dc = GetDC(0);
        try
        {
            fixed (byte* p = target)
                return GetDIBits(dc, bitmap, 0, (uint)height, p, &header, 0) == height;
        }
        finally
        {
            ReleaseDC(0, dc);
        }
    }

    // ---------- interop ----------

    private const uint SHGFI_SYSICONINDEX = 0x4000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint SHGFI_OVERLAYINDEX = 0x40;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const int SHIL_LARGE = 0;
    private const int SHIL_SMALL = 1;
    private const uint ILD_TRANSPARENT = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHFILEINFOW
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        public fixed char szDisplayName[260];
        public fixed char szTypeName[80];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int fIcon;
        public int xHotspot;
        public int yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    /// <summary>The header plus room for the color masks GetDIBits may write after it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
        private fixed uint _colors[4];
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SHGetFileInfoW(string path, uint attributes, SHFILEINFOW* info, uint size, uint flags);

    [LibraryImport("shell32.dll")]
    private static partial int SHGetImageList(int list, in Guid riid, out nint ppv);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(nint icon, out ICONINFO info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(nint obj, int size, void* buffer);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, void* bits, BITMAPINFO* info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);
}

[GeneratedComInterface]
[Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
internal partial interface IImageList
{
    [PreserveSig] int Add(nint image, nint mask, out int index);
    [PreserveSig] int ReplaceIcon(int i, nint icon, out int index);
    [PreserveSig] int SetOverlayImage(int image, int overlay);
    [PreserveSig] int Replace(int i, nint image, nint mask);
    [PreserveSig] int AddMasked(nint image, uint mask, out int index);
    [PreserveSig] int Draw(nint parameters);
    [PreserveSig] int Remove(int i);
    [PreserveSig] int GetIcon(int i, uint flags, out nint icon);
}
