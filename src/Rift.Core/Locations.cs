using Rift.Core.Localization;

namespace Rift.Core;

public enum LocationKind
{
    /// <summary>A real folder on disk or a share.</summary>
    Folder,
    /// <summary>The list of drives.</summary>
    ThisPc,
    /// <summary>Deleted files, from every drive's $Recycle.Bin.</summary>
    RecycleBin,
    /// <summary>Results of a recursive search: real files from many folders.</summary>
    Search,
    /// <summary>Computers on the local network.</summary>
    Network,
    /// <summary>A computer's shares: <c>\\server</c>.</summary>
    Server,
    /// <summary>The list of Windows libraries.</summary>
    Libraries,
    /// <summary>One library: the contents of all its folders together.</summary>
    Library,
    /// <summary>Every file and folder carrying one tag.</summary>
    Tag,
}

/// <summary>
/// Places that aren't folders on disk. They use Explorer's own "shell:" names,
/// so they read sensibly anywhere a path is shown and can be pasted into Explorer.
/// </summary>
public static class Locations
{
    public const string ThisPc = "shell:MyComputerFolder";
    public const string RecycleBin = "shell:RecycleBinFolder";
    public const string Network = "shell:NetworkPlacesFolder";
    public const string LibrariesFolder = "shell:Libraries";
    private const string SearchPrefix = "search:";
    private const string LibraryPrefix = "library:";
    private const string TagPrefix = "tag:";

    /// <summary>A tag tab's path: <c>tag:id|Name</c> (the name is only for the title).</summary>
    public static string Tag(string id, string name) => $"{TagPrefix}{id}|{name}";

    public static (string Id, string Name)? ParseTag(string path)
    {
        if (!path.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var bar = path.IndexOf('|');
        return bar < 0 ? (path[TagPrefix.Length..], path[TagPrefix.Length..]) : (path[TagPrefix.Length..bar], path[(bar + 1)..]);
    }

    /// <summary>A library tab's path: <c>library:Documents|C:\…\Documents.library-ms</c> (display name, then the file).</summary>
    public static string Library(string name, string file) => $"{LibraryPrefix}{name}|{file}";

    public static (string Name, string File)? ParseLibrary(string path)
    {
        if (!path.StartsWith(LibraryPrefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var bar = path.LastIndexOf('|');
        return bar < LibraryPrefix.Length ? null : (path[LibraryPrefix.Length..bar], path[(bar + 1)..]);
    }

    /// <summary><c>\\server</c> on its own, without a share: what Windows shows as a computer.</summary>
    public static bool IsServer(string path)
    {
        var p = path.TrimEnd('\\');
        return p.Length > 2 && p.StartsWith(@"\\", StringComparison.Ordinal) && p.IndexOf('\\', 2) < 0;
    }

    /// <summary>A search tab's path: <c>search:query|C:\root</c>. '|' can't appear in a Windows path, so the last one splits.</summary>
    public static string Search(string root, string query) => $"{SearchPrefix}{query}|{root}";

    public static (string Root, string Query)? ParseSearch(string path)
    {
        if (!path.StartsWith(SearchPrefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var bar = path.LastIndexOf('|');
        return bar < SearchPrefix.Length ? null : (path[(bar + 1)..], path[SearchPrefix.Length..bar]);
    }

    public static LocationKind KindOf(string path) => path switch
    {
        _ when string.Equals(path, ThisPc, StringComparison.OrdinalIgnoreCase) => LocationKind.ThisPc,
        _ when string.Equals(path, RecycleBin, StringComparison.OrdinalIgnoreCase) => LocationKind.RecycleBin,
        _ when ParseSearch(path) is not null => LocationKind.Search,
        _ when string.Equals(path, Network, StringComparison.OrdinalIgnoreCase) => LocationKind.Network,
        _ when string.Equals(path, LibrariesFolder, StringComparison.OrdinalIgnoreCase) => LocationKind.Libraries,
        _ when ParseLibrary(path) is not null => LocationKind.Library,
        _ when ParseTag(path) is not null => LocationKind.Tag,
        _ when IsServer(path) => LocationKind.Server,
        _ => LocationKind.Folder,
    };

    public static bool IsVirtual(string path) => KindOf(path) != LocationKind.Folder;

    public static string Title(string path) => KindOf(path) switch
    {
        LocationKind.ThisPc => Strings.PlaceThisPc,
        LocationKind.RecycleBin => Strings.PlaceRecycleBin,
        LocationKind.Search => Strings.SearchTitle(ParseSearch(path)!.Value.Query),
        LocationKind.Network => Strings.PlaceNetwork,
        LocationKind.Libraries => Strings.PlaceLibraries,
        LocationKind.Library => ParseLibrary(path)!.Value.Name,
        LocationKind.Tag => ParseTag(path)!.Value.Name,
        LocationKind.Server => path.TrimEnd('\\')[2..],
        _ => PathCrumbs.Title(path),
    };

    public static IReadOnlyList<PathCrumb> Crumbs(string path) => KindOf(path) switch
    {
        LocationKind.Folder => PathCrumbs.Split(path),
        // The folder searched in stays clickable, the results are the last crumb.
        LocationKind.Search => [.. Crumbs(ParseSearch(path)!.Value.Root), new PathCrumb(Title(path), path)],
        LocationKind.Library => [new PathCrumb(Strings.PlaceLibraries, LibrariesFolder), new PathCrumb(Title(path), path)],
        LocationKind.Server => [new PathCrumb(Strings.PlaceNetwork, Network), new PathCrumb(Title(path), path.TrimEnd('\\'))],
        _ => [new PathCrumb(Title(path), path)],
    };

    /// <summary>One level up. From a drive root that's This PC, as in Explorer.</summary>
    public static string? Parent(string path)
    {
        if (ParseSearch(path) is { } search)
            return search.Root;
        switch (KindOf(path))
        {
            case LocationKind.Library:
                return LibrariesFolder;
            case LocationKind.Server:
                return Network;
            case not LocationKind.Folder:
                return null;
        }
        if (PathCrumbs.Parent(path) is { } parent)
            return parent;
        // A share root (\\nas\media) goes up to its server; a drive root to This PC.
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var p = path.TrimEnd('\\');
            var cut = p.IndexOf('\\', 2);
            return cut > 0 ? p[..cut] : null;
        }
        return ThisPc;
    }

    /// <summary>Drives as rows: free space in the Modified column, the drive type in Type, capacity in Size.</summary>
    /// <remarks>A drive that doesn't answer in time (a sleeping network share) is listed by its letter alone; Ctrl+R asks again.</remarks>
    public static IReadOnlyList<FileEntry> ReadDrives()
    {
        var entries = new List<FileEntry>();
        foreach (var drive in DriveReader.Read(DriveReader.Patience))
        {
            var type = drive.Type switch
            {
                DriveType.Network => Strings.DriveNetwork,
                DriveType.Removable => Strings.DriveRemovable,
                DriveType.CDRom => Strings.DriveOptical,
                _ => Strings.DriveLocal,
            };
            if (!drive.IsReady)
            {
                entries.Add(new FileEntry(drive.Letter, drive.Name, true, null, default, FileKind.Folder, false,
                    FileAttributes.Directory, DateNote: "", TypeNote: type));
                continue;
            }

            var label = string.IsNullOrWhiteSpace(drive.Label) ? type : drive.Label;
            entries.Add(new FileEntry($"{label} ({drive.Letter})", drive.Name, true, drive.TotalSize, default, FileKind.Folder, false,
                FileAttributes.Directory, DateNote: Strings.DriveFreeShort(SizeFormatter.Format(drive.FreeSpace)), TypeNote: type));
        }
        return entries;
    }
}
