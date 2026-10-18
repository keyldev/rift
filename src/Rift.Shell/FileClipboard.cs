using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Rift.Shell.Native;

namespace Rift.Shell;

/// <summary>
/// Files on the Windows clipboard in the same format Explorer uses (CF_HDROP + "Preferred DropEffect"),
/// so Ctrl+C in Rift → Ctrl+V in Explorer works, and the other way around, including Cut.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe class FileClipboard
{
    private static readonly uint PreferredDropEffect = RegisterClipboardFormatW("Preferred DropEffect");

    public static bool HasFiles => IsClipboardFormatAvailable(CF_HDROP);

    /// <param name="owner">Window handle; Windows refuses clipboard data without an owner.</param>
    public static bool SetFiles(nint owner, IReadOnlyList<string> paths, bool cut)
    {
        if (paths.Count == 0 || !Open(owner))
            return false;
        try
        {
            EmptyClipboard();

            var drop = CreateHDrop(paths);
            if (SetClipboardData(CF_HDROP, drop) == 0)
            {
                GlobalFree(drop);
                return false;
            }

            var effect = GlobalAlloc(GMEM_MOVEABLE, sizeof(int));
            *(int*)GlobalLock(effect) = cut ? DROPEFFECT_MOVE : DROPEFFECT_COPY | DROPEFFECT_LINK;
            GlobalUnlock(effect);
            if (SetClipboardData(PreferredDropEffect, effect) == 0)
                GlobalFree(effect);
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <returns>Files on the clipboard and whether they were cut, or null if there are none.</returns>
    public static (IReadOnlyList<string> Paths, bool Cut)? GetFiles(nint owner)
    {
        if (!HasFiles || !Open(owner))
            return null;
        try
        {
            var drop = GetClipboardData(CF_HDROP);
            if (drop == 0)
                return null;

            var count = DragQueryFileW(drop, uint.MaxValue, null, 0);
            var paths = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                var length = DragQueryFileW(drop, i, null, 0);
                var buffer = new char[length + 1];
                fixed (char* b = buffer)
                    DragQueryFileW(drop, i, b, length + 1);
                paths.Add(new string(buffer, 0, (int)length));
            }

            var cut = false;
            var effectHandle = GetClipboardData(PreferredDropEffect);
            if (effectHandle != 0 && GlobalLock(effectHandle) is var p && p != 0)
            {
                var effect = *(int*)p;
                GlobalUnlock(effectHandle);
                cut = (effect & DROPEFFECT_MOVE) != 0 && (effect & DROPEFFECT_COPY) == 0;
            }

            return (paths, cut);
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static void Clear(nint owner)
    {
        if (!Open(owner))
            return;
        EmptyClipboard();
        CloseClipboard();
    }

    private static bool Open(nint owner)
    {
        // Another app may hold the clipboard for a moment; retry briefly like Explorer does.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(owner))
                return true;
            Thread.Sleep(15);
        }
        return false;
    }

    private static nint CreateHDrop(IReadOnlyList<string> paths)
    {
        var header = sizeof(DROPFILES);
        var chars = paths.Sum(p => p.Length + 1) + 1;
        var handle = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (nuint)(header + chars * sizeof(char)));
        var ptr = (byte*)GlobalLock(handle);

        *(DROPFILES*)ptr = new DROPFILES { pFiles = (uint)header, fWide = 1 };
        var dest = (char*)(ptr + header);
        foreach (var path in paths)
        {
            path.AsSpan().CopyTo(new Span<char>(dest, path.Length));
            dest += path.Length + 1; // memory is zeroed, so the NUL is already there
        }

        GlobalUnlock(handle);
        return handle;
    }
}
