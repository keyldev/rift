using System.Buffers.Binary;
using System.Text;

namespace Rift.Core.Operations;

/// <summary>A deleted file or folder sitting in a drive's Recycle Bin.</summary>
/// <param name="DataPath">The $R… file or folder holding the content.</param>
/// <param name="InfoPath">The $I… file next to it: original path, size, deletion time.</param>
public sealed record RecycledItem(string DataPath, string InfoPath, string OriginalPath, long Size, DateTime DeletedAt, bool IsDirectory)
{
    public string Name => Path.GetFileName(OriginalPath.TrimEnd('\\'));
}

/// <summary>
/// Reads and restores the Recycle Bin the way Explorer stores it: every drive has
/// <c>X:\$Recycle.Bin\&lt;user SID&gt;</c> with a $I file (metadata) and a $R file (data) per deleted item.
/// Working with the files directly is fast, needs no shell UI and lets Ctrl+Z find what Del just sent there.
/// </summary>
public static class RecycleBinStore
{
    /// <summary>Lists deleted items in the given bin folders (one per drive), newest first.</summary>
    public static IReadOnlyList<RecycledItem> Read(IEnumerable<string> binFolders)
    {
        var items = new List<RecycledItem>();
        foreach (var folder in binFolders)
        {
            IEnumerable<string> infos;
            try
            {
                infos = Directory.EnumerateFiles(folder, "$I*").ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var info in infos)
            {
                var data = Path.Combine(folder, "$R" + Path.GetFileName(info)[2..]);
                var isDir = Directory.Exists(data);
                if (!isDir && !File.Exists(data))
                    continue; // metadata without content: an interrupted delete
                try
                {
                    if (ParseInfo(File.ReadAllBytes(info)) is { } parsed)
                        items.Add(new RecycledItem(data, info, parsed.OriginalPath, parsed.Size, parsed.DeletedAt, isDir));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Being written right now, or not ours to read.
                }
            }
        }

        items.Sort((a, b) => b.DeletedAt.CompareTo(a.DeletedAt));
        return items;
    }

    /// <summary>
    /// Parses a $I file. Version 1 (Vista–8.1): fixed 260-char path; version 2 (Windows 10+): length-prefixed path.
    /// </summary>
    public static (string OriginalPath, long Size, DateTime DeletedAt)? ParseInfo(ReadOnlySpan<byte> data)
    {
        if (data.Length < 24)
            return null;
        var version = BinaryPrimitives.ReadInt64LittleEndian(data);
        var size = BinaryPrimitives.ReadInt64LittleEndian(data[8..]);
        var deleted = DateTime.FromFileTime(BinaryPrimitives.ReadInt64LittleEndian(data[16..]));

        ReadOnlySpan<byte> chars;
        switch (version)
        {
            case 1 when data.Length >= 24 + 520:
                chars = data.Slice(24, 520);
                break;
            case 2 when data.Length >= 28:
                var length = BinaryPrimitives.ReadInt32LittleEndian(data[24..]);
                if (length <= 0 || data.Length < 28 + length * 2)
                    return null;
                chars = data.Slice(28, length * 2);
                break;
            default:
                return null;
        }

        var path = Encoding.Unicode.GetString(chars);
        var nul = path.IndexOf('\0');
        if (nul >= 0)
            path = path[..nul];
        return path.Length == 0 ? null : (path, size, deleted);
    }

    /// <summary>Writes a version 2 $I file; used by tests to build a fake bin.</summary>
    public static byte[] FormatInfo(string originalPath, long size, DateTime deletedAt)
    {
        var chars = originalPath.Length + 1;
        var data = new byte[28 + chars * 2];
        BinaryPrimitives.WriteInt64LittleEndian(data, 2);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), size);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), deletedAt.ToFileTime());
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), chars);
        Encoding.Unicode.GetBytes(originalPath, data.AsSpan(28));
        return data;
    }

    /// <summary>
    /// Puts an item back where it was deleted from. If something else took its place meanwhile,
    /// it comes back as "name (2)", so nothing is overwritten.
    /// </summary>
    /// <returns>Where the item ended up.</returns>
    public static string Restore(RecycledItem item)
    {
        var parent = Path.GetDirectoryName(item.OriginalPath) ?? throw new IOException($"'{item.OriginalPath}' has no folder");
        Directory.CreateDirectory(parent);
        var target = FileNames.Exists(item.OriginalPath)
            ? Path.Combine(parent, FileNames.Unique(parent, item.Name, item.IsDirectory))
            : item.OriginalPath;

        OperationJournal.MoveExact(item.DataPath, target);
        TryDelete(item.InfoPath);
        return target;
    }

    /// <summary>Deletes an item from the bin for good.</summary>
    public static void Delete(RecycledItem item)
    {
        if (item.IsDirectory)
        {
            foreach (var file in Directory.EnumerateFiles(item.DataPath, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(item.DataPath, recursive: true);
        }
        else
        {
            File.SetAttributes(item.DataPath, FileAttributes.Normal);
            File.Delete(item.DataPath);
        }
        TryDelete(item.InfoPath);
    }

    /// <summary>
    /// Finds what a delete just sent to the bin: the newest entry per original path, deleted after <paramref name="since"/>.
    /// </summary>
    public static IReadOnlyList<RecycledItem> FindRecent(IEnumerable<string> binFolders, IEnumerable<string> originalPaths, DateTime since)
    {
        var wanted = originalPaths.Select(p => p.TrimEnd('\\')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Read(binFolders)
            .Where(i => i.DeletedAt >= since && wanted.Contains(i.OriginalPath.TrimEnd('\\')))
            .GroupBy(i => i.OriginalPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()) // Read sorts newest first
            .ToList();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover $I without its $R is ignored by Explorer and by Read.
        }
    }
}
