namespace Rift.Core;

public enum AddressKind
{
    NotFound,
    Folder,
    /// <summary>A file: open its folder and select it.</summary>
    File,
}

/// <summary>What the address bar does with typed text, and what it suggests while typing.</summary>
public static class AddressResolver
{
    /// <summary>
    /// Turns typed text into a place: absolute or relative paths (against <paramref name="current"/>),
    /// "..", %ENV% variables, quotes, "~" for home, and Rift's own shell: places.
    /// </summary>
    public static (AddressKind Kind, string Path) Resolve(string text, string current)
    {
        var path = Normalize(text, current);
        if (path is null)
            return (AddressKind.NotFound, text);
        switch (Locations.KindOf(path))
        {
            case LocationKind.ThisPc:
                return (AddressKind.Folder, Locations.ThisPc);
            case LocationKind.RecycleBin:
                return (AddressKind.Folder, Locations.RecycleBin);
            case LocationKind.Network:
                return (AddressKind.Folder, Locations.Network);
            case LocationKind.Libraries:
                return (AddressKind.Folder, Locations.LibrariesFolder);
            case LocationKind.Server:
                return (AddressKind.Folder, path.TrimEnd('\\'));
            case not LocationKind.Folder:
                return (AddressKind.NotFound, path);
        }
        if (Directory.Exists(path))
            return (AddressKind.Folder, path.Length > 3 ? path.TrimEnd('\\') : path);
        if (File.Exists(path))
            return (AddressKind.File, path);
        return (AddressKind.NotFound, path);
    }

    /// <summary>Subfolders of the typed folder whose names start with what follows the last backslash.</summary>
    public static IReadOnlyList<string> Suggest(string text, string current, int limit = 20)
    {
        var expanded = Expand(text);
        if (expanded.Length == 0 || Locations.IsVirtual(expanded))
            return [];

        var cut = expanded.LastIndexOfAny(['\\', '/']);
        if (cut < 0)
            return [];
        var folderPart = expanded[..(cut + 1)];
        var prefix = expanded[(cut + 1)..];
        var folder = Normalize(folderPart, current);
        if (folder is null || Locations.IsVirtual(folder))
            return [];

        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            return new DirectoryInfo(folder)
                .EnumerateDirectories(prefix.Length == 0 ? "*" : prefix + "*", options)
                .Select(d => folderPart + d.Name)
                .Order(NaturalStringComparer.Instance)
                .Take(limit)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static string Expand(string text)
    {
        var t = text.Trim().Trim('"').Trim();
        t = Environment.ExpandEnvironmentVariables(t);
        if (t == "~" || t.StartsWith(@"~\", StringComparison.Ordinal) || t.StartsWith("~/", StringComparison.Ordinal))
            t = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + t[1..];
        return t;
    }

    private static string? Normalize(string text, string current)
    {
        var t = Expand(text);
        if (t.Length == 0)
            return null;
        if (Locations.IsVirtual(t))
            return t.TrimEnd('\\');
        if (t is [_, ':'])
            t += '\\';
        try
        {
            var baseFolder = Locations.IsVirtual(current) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : current;
            return Path.GetFullPath(Path.IsPathRooted(t) ? t : Path.Combine(baseFolder, t));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
