using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace Rift.Shell;

/// <summary>
/// Thumbnails as Explorer makes them (IShellItemImageFactory): photos, video frames, PDF pages,
/// Office documents, whatever thumbnail handlers are installed; the file's icon when there's none.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class ShellThumbnails
{
    /// <summary>Renders a thumbnail fitting a square of <paramref name="size"/> pixels. Call from an STA thread.</summary>
    /// <param name="thumbnailOnly">Return null instead of falling back to the icon.</param>
    /// <returns>Pixels with premultiplied alpha, or null.</returns>
    public static IconPixels? Load(string path, int size, bool thumbnailOnly = false)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, 0, iid, out var ptr) != Com.S_OK || ptr == 0)
            return null;
        var factory = Com.Wrap<IShellItemImageFactory>(ptr);
        try
        {
            var flags = SIIGBF_RESIZETOFIT | (thumbnailOnly ? SIIGBF_THUMBNAILONLY : 0);
            if (factory.GetImage(new SIZE { cx = size, cy = size }, flags, out var bitmap) != Com.S_OK || bitmap == 0)
                return null;
            try
            {
                return Read(bitmap);
            }
            finally
            {
                DeleteObject(bitmap);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            Com.Free(factory);
        }
    }

    private static IconPixels? Read(nint bitmap)
    {
        BITMAP bm;
        if (GetObjectW(bitmap, sizeof(BITMAP), &bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight <= 0)
            return null;
        int w = bm.bmWidth, h = bm.bmHeight;
        var pixels = new byte[w * h * 4];
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = w,
            biHeight = -h,
            biPlanes = 1,
            biBitCount = 32,
        };
        var dc = GetDC(0);
        try
        {
            fixed (byte* p = pixels)
            {
                if (GetDIBits(dc, bitmap, 0, (uint)h, p, &header, 0) != h)
                    return null;
            }
        }
        finally
        {
            ReleaseDC(0, dc);
        }

        // Images without an alpha channel come back fully transparent: make them opaque.
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length && !hasAlpha; i += 4)
            hasAlpha = pixels[i] != 0;
        if (!hasAlpha)
        {
            for (var i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
        }
        return new IconPixels(w, h, pixels);
    }

    private const int SIIGBF_RESIZETOFIT = 0x0;
    private const int SIIGBF_THUMBNAILONLY = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE
    {
        public int cx;
        public int cy;
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
        private fixed uint _colors[4];
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid riid, out nint ppv);

    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(nint obj, int size, void* buffer);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, void* bits, BITMAPINFOHEADER* info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint dc);
}

[GeneratedComInterface]
[Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
internal partial interface IShellItemImageFactory
{
    [PreserveSig] int GetImage(ShellThumbnails.SIZE size, int flags, out nint bitmap);
}
