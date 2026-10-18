using Rift.Core;
using Rift.Core.Localization;
using System.Runtime.Versioning;
using Rift.Shell;

namespace Rift.App.Services;

/// <summary>Listings for the places that aren't folders: Network, a server's shares, Libraries, one library.</summary>
public static class Places
{
    [SupportedOSPlatform("windows")]
    private static readonly Lazy<StaWorker> Worker = new(() => new StaWorker("Rift network"));

    public static Task<IReadOnlyList<FileEntry>> ReadAsync(string path, bool includeHidden, CancellationToken ct) =>
        OperatingSystem.IsWindows() ? ReadWindowsAsync(path, includeHidden, ct) : Task.FromResult<IReadOnlyList<FileEntry>>([]);

    [SupportedOSPlatform("windows")]
    private static Task<IReadOnlyList<FileEntry>> ReadWindowsAsync(string path, bool includeHidden, CancellationToken ct)
    {
        return Locations.KindOf(path) switch
        {
            // Shell enumeration of the network wants an STA thread.
            LocationKind.Network => Worker.Value.Invoke(() => (IReadOnlyList<FileEntry>)ShellPlaces.Computers()
                .Select(c => Place(c.Name, c.Path, Strings.KindComputer)).ToList()),
            LocationKind.Server => Task.Run(() => (IReadOnlyList<FileEntry>)ShellPlaces.Shares(path)
                .Select(s => Place(s.Name, s.Path, Strings.KindShare, s.Comment)).ToList(), ct),
            LocationKind.Libraries => Task.Run(() => (IReadOnlyList<FileEntry>)ReadLibraries()
                .Select(l => Place(l.Name, Locations.Library(l.Name, l.File), Strings.KindLibrary, Strings.LibraryFolders(l.Folders.Count)))
                .ToList(), ct),
            LocationKind.Library => Task.Run(() => Load(path) is { } library ? Libraries.ReadContents(library, includeHidden) : [], ct),
            _ => Task.FromResult<IReadOnlyList<FileEntry>>([]),
        };
    }

    /// <summary>The real folders behind a library tab, to watch for changes.</summary>
    public static IReadOnlyList<string> LibraryFolders(string path) =>
        Load(path)?.Folders.Where(Directory.Exists).ToList() ?? [];

    private static FileEntry Place(string name, string path, string type, string? note = null) =>
        new(name, path, true, null, default, FileKind.Folder, false, FileAttributes.Directory, DateNote: note ?? "", TypeNote: type);

    private static IEnumerable<LibraryInfo> ReadLibraries()
    {
        if (!OperatingSystem.IsWindows() || ShellPlaces.KnownFolderPath(ShellPlaces.LibrariesFolder) is not { } folder || !Directory.Exists(folder))
            return [];
        return Directory.EnumerateFiles(folder, "*.library-ms").Select(Parse).OfType<LibraryInfo>().OrderBy(l => l.Name, NaturalStringComparer.Instance);
    }

    private static LibraryInfo? Load(string path) => Locations.ParseLibrary(path) is { } library ? Parse(library.File) : null;

    private static LibraryInfo? Parse(string file)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            return Libraries.Parse(file, File.ReadAllText(file), ShellPlaces.KnownFolderPath, ShellPlaces.IndirectString);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
