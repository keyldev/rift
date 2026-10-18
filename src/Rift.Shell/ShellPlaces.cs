using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Rift.Shell;

/// <summary>A computer on the network or a share on it.</summary>
public sealed record NetworkItem(string Name, string Path, string? Comment = null);

/// <summary>Known folders, localized resource strings and the network neighbourhood.</summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class ShellPlaces
{
    public static readonly Guid LibrariesFolder = new("1B3EA5DC-B587-4786-B4EF-BD1DC332AEAE");
    private static readonly Guid NetworkFolder = new("D20BEEC4-5CA8-4905-AE3B-BF251EA09B53");

    public static string? KnownFolderPath(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, 0, out var path) != 0)
            return null;
        try
        {
            return Marshal.PtrToStringUni(path);
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    /// <summary>"@shell32.dll,-34575" → "Documents", in the Windows display language.</summary>
    public static string? IndirectString(string source)
    {
        var buffer = stackalloc char[512];
        return SHLoadIndirectString(source, buffer, 512, 0) == 0 ? new string(buffer) : null;
    }

    /// <summary>
    /// Computers Explorer sees under Network. Slow (network discovery) and often empty when discovery
    /// is off; call from an STA background thread.
    /// </summary>
    public static IReadOnlyList<NetworkItem> Computers()
    {
        var result = new List<NetworkItem>();
        if (SHGetKnownFolderIDList(NetworkFolder, 0, 0, out var root) != 0)
            return result;
        IShellFolder? folder = null;
        try
        {
            if (SHBindToObject(0, root, 0, typeof(IShellFolder).GUID, out var folderPtr) != 0)
                return result;
            folder = Com.Wrap<IShellFolder>(folderPtr);
            if (folder.EnumObjects(0, SHCONTF_FOLDERS | SHCONTF_NONFOLDERS, out var enumPtr) != 0 || enumPtr == 0)
                return result;
            var items = Com.Wrap<IEnumIDList>(enumPtr);
            try
            {
                while (items.Next(1, out var child, out var fetched) == 0 && fetched == 1)
                {
                    try
                    {
                        var path = DisplayName(folder, child, SHGDN_FORPARSING);
                        // Only plain \\COMPUTER entries; media servers and devices have other parsing names.
                        if (path is { Length: > 2 } && path.StartsWith(@"\\", StringComparison.Ordinal) && path.IndexOf('\\', 2) < 0)
                            result.Add(new NetworkItem(DisplayName(folder, child, SHGDN_NORMAL) ?? path[2..], path));
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(child);
                    }
                }
            }
            finally
            {
                Com.Free(items);
            }
        }
        finally
        {
            Com.Free(folder);
            Marshal.FreeCoTaskMem(root);
        }
        return result;
    }

    /// <summary>The disk shares a server offers, without hidden ones (C$, ADMIN$, IPC$).</summary>
    public static IReadOnlyList<NetworkItem> Shares(string server)
    {
        var name = server.TrimStart('\\');
        var result = new List<NetworkItem>();
        uint resume = 0;
        var status = NetShareEnum(name, 1, out var buffer, uint.MaxValue, out var read, out _, ref resume);
        if (status is not (0 or ERROR_MORE_DATA))
            throw new IOException(new System.ComponentModel.Win32Exception((int)status).Message);
        try
        {
            var shares = (SHARE_INFO_1*)buffer;
            for (var i = 0; i < read; i++)
            {
                var share = shares[i];
                var shareName = Marshal.PtrToStringUni(share.netname) ?? "";
                if ((share.type & 0xFF) != STYPE_DISKTREE || (share.type & STYPE_SPECIAL) != 0 || shareName.EndsWith('$'))
                    continue;
                result.Add(new NetworkItem(shareName, $@"\\{name}\{shareName}", Marshal.PtrToStringUni(share.remark)));
            }
        }
        finally
        {
            NetApiBufferFree(buffer);
        }
        return result;
    }

    private static string? DisplayName(IShellFolder folder, nint child, uint flags)
    {
        var strret = stackalloc byte[STRRET_SIZE];
        if (folder.GetDisplayNameOf(child, flags, (nint)strret) != 0)
            return null;
        return StrRetToStrW((nint)strret, child, out var text) == 0 ? TakeString(text) : null;
    }

    private static string? TakeString(nint text)
    {
        try
        {
            return Marshal.PtrToStringUni(text);
        }
        finally
        {
            Marshal.FreeCoTaskMem(text);
        }
    }

    private const uint SHCONTF_FOLDERS = 0x20;
    private const uint SHCONTF_NONFOLDERS = 0x40;
    private const uint SHGDN_NORMAL = 0;
    private const uint SHGDN_FORPARSING = 0x8000;
    private const int STRRET_SIZE = 272; // uType + MAX_PATH wide chars, with room for alignment
    private const uint ERROR_MORE_DATA = 234;
    private const uint STYPE_DISKTREE = 0;
    private const uint STYPE_SPECIAL = 0x80000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHARE_INFO_1
    {
        public nint netname;
        public uint type;
        public nint remark;
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderIDList(in Guid id, uint flags, nint token, out nint pidl);

    [LibraryImport("shell32.dll")]
    private static partial int SHBindToObject(nint folder, nint pidl, nint bindContext, in Guid riid, out nint ppv);

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHLoadIndirectString(string source, char* output, uint size, nint reserved);

    [LibraryImport("shlwapi.dll")]
    private static partial int StrRetToStrW(nint strret, nint pidl, out nint text);

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint NetShareEnum(string server, uint level, out nint buffer, uint maxLength,
        out uint entriesRead, out uint totalEntries, ref uint resumeHandle);

    [LibraryImport("netapi32.dll")]
    private static partial uint NetApiBufferFree(nint buffer);
}

[System.Runtime.InteropServices.Marshalling.GeneratedComInterface]
[Guid("000214F2-0000-0000-C000-000000000046")]
internal partial interface IEnumIDList
{
    [PreserveSig] int Next(uint count, out nint pidl, out uint fetched);
    [PreserveSig] int Skip(uint count);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out nint enumerator);
}
