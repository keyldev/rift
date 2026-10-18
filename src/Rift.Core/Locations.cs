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
}

/// <summary>
/// Places that aren't folders on disk. They use Explorer's own "shell:" names,
/// so they read sensibly anywhere a path is shown and can be pasted into Explorer.
/// </summary>
public static class Locations
{
    public const string ThisPc = "shell:MyComputerFolder";
    public const string RecycleBin = "shell:RecycleBinFolder";

    public static LocationKind KindOf(string path) => path switch
    {
        _ when string.Equals(path, ThisPc, StringComparison.OrdinalIgnoreCase) => LocationKind.ThisPc,
        _ when string.Equals(path, RecycleBin, StringComparison.OrdinalIgnoreCase) => LocationKind.RecycleBin,
        _ => LocationKind.Folder,
    };

    public static bool IsVirtual(string path) => KindOf(path) != LocationKind.Folder;

    public static string Title(string path) => KindOf(path) switch
    {
        LocationKind.ThisPc => Strings.PlaceThisPc,
        LocationKind.RecycleBin => Strings.PlaceRecycleBin,
        _ => PathCrumbs.Title(path),
    };

    public static IReadOnlyList<PathCrumb> Crumbs(string path) =>
        IsVirtual(path) ? [new PathCrumb(Title(path), path)] : PathCrumbs.Split(path);

    /// <summary>One level up. From a drive root that's This PC, as in Explorer.</summary>
    public static string? Parent(string path)
    {
        if (IsVirtual(path))
            return null;
        return PathCrumbs.Parent(path) ?? (path.StartsWith(@"\\", StringComparison.Ordinal) ? null : ThisPc);
    }

    /// <summary>Drives as rows: free space in the Modified column, the drive type in Type, capacity in Size.</summary>
    public static IReadOnlyList<FileEntry> ReadDrives()
    {
        var entries = new List<FileEntry>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                var letter = drive.Name.TrimEnd('\\');
                var type = drive.DriveType switch
                {
                    DriveType.Network => Strings.DriveNetwork,
                    DriveType.Removable => Strings.DriveRemovable,
                    DriveType.CDRom => Strings.DriveOptical,
                    _ => Strings.DriveLocal,
                };
                if (!drive.IsReady)
                {
                    entries.Add(new FileEntry(letter, drive.Name, true, null, default, FileKind.Folder, false,
                        FileAttributes.Directory, DateNote: "", TypeNote: type));
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? type : drive.VolumeLabel;
                entries.Add(new FileEntry($"{label} ({letter})", drive.Name, true, drive.TotalSize, default, FileKind.Folder, false,
                    FileAttributes.Directory, DateNote: Strings.DriveFreeShort(SizeFormatter.Format(drive.AvailableFreeSpace)), TypeNote: type));
            }
            catch (IOException)
            {
                // Unplugged while we were looking at it.
            }
        }
        return entries;
    }
}
