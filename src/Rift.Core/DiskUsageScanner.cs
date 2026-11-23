using System.Diagnostics;
using System.IO.Enumeration;
using Rift.Core.Localization;

namespace Rift.Core;

public enum DiskUsageCategory
{
    Free,
    Apps,
    Media,
    Archives,
    Documents,
    Other,
    System,
}

public enum DiskUsageSort
{
    Size,
    Count,
    Name,
}

/// <summary>One category slice of a drive scan.</summary>
public sealed record DiskUsageBucket(DiskUsageCategory Category, long Bytes, long FileCount);

/// <summary>
/// A point-in-time snapshot of a drive usage scan. While <see cref="IsComplete"/> is false,
/// <see cref="DiskUsageCategory.System"/> represents the remaining used space not yet scanned;
/// once complete, it represents space reported as used by the OS that couldn't be enumerated
/// (such as pagefile, System Volume Information, or inaccessible folders).
/// </summary>
public sealed record DiskUsageSnapshot(
    string RootPath,
    long TotalBytes,
    long FreeBytes,
    long ScannedBytes,
    long ScannedFiles,
    long ScannedFolders,
    long SkippedEntries,
    bool IsComplete,
    IReadOnlyList<DiskUsageBucket> Buckets)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    /// <summary>Total bytes represented by the buckets (at least <see cref="TotalBytes"/>).</summary>
    public long AccountedTotalBytes => Math.Max(TotalBytes, ScannedBytes + FreeBytes);
}

/// <summary>
/// Maps file names to disk-usage categories. Kept separate from <see cref="FileKinds"/> so adding
/// binary/library extensions (.dll, .sys, ...) doesn't alter Type column labels in file lists.
/// </summary>
public static class DiskUsageClassifier
{
    private static readonly Dictionary<string, DiskUsageCategory> ByExtension = Build(
        (DiskUsageCategory.Apps, [
            "exe", "dll", "msi", "msix", "appx", "appxbundle", "sys", "ocx", "cpl", "drv",
            "scr", "com", "bat", "cmd", "jar", "so", "dylib", "pyd", "node", "wasm",
            "apk", "msp", "vsix", "lnk", "ps1",
        ]),
        (DiskUsageCategory.Media, [
            // Images
            "png", "jpg", "jpeg", "gif", "webp", "bmp", "svg", "ico", "heic", "heif",
            "tiff", "tif", "avif", "raw", "cr2", "nef", "arw", "dng", "psd", "ai",
            // Video
            "mp4", "mkv", "avi", "mov", "webm", "wmv", "m4v", "flv", "mpg", "mpeg",
            "ts", "m2ts", "vob", "3gp",
            // Audio
            "mp3", "flac", "wav", "ogg", "m4a", "aac", "opus", "wma", "aiff", "alac",
            "mid", "midi",
        ]),
        (DiskUsageCategory.Archives, [
            "zip", "7z", "rar", "tar", "gz", "tgz", "xz", "bz2", "zst", "lz4",
            "iso", "img", "vhd", "vhdx", "vmdk", "wim", "esd", "cab", "dmg",
        ]),
        (DiskUsageCategory.Documents, [
            // Documents & ebooks
            "pdf", "txt", "md", "rtf", "doc", "docx", "odt", "xls", "xlsx", "ods",
            "csv", "ppt", "pptx", "odp", "epub", "djvu", "fb2", "log", "ini", "cfg", "conf",
            // Source code & markup
            "cs", "csproj", "sln", "slnx", "props", "targets", "axaml", "xaml",
            "js", "mjs", "cjs", "jsx", "tsx", "json", "jsonc", "xml", "yml", "yaml", "toml",
            "py", "rs", "go", "c", "cpp", "cc", "h", "hpp", "java", "kt", "swift",
            "rb", "php", "sh", "html", "htm", "css", "scss", "less", "sql",
        ]));

    private static readonly Dictionary<string, DiskUsageCategory>.AlternateLookup<ReadOnlySpan<char>> SpanLookup =
        ByExtension.GetAlternateLookup<ReadOnlySpan<char>>();

    public static DiskUsageCategory Classify(ReadOnlySpan<char> fileName)
    {
        var dot = fileName.LastIndexOf('.');
        if (dot < 0 || dot == fileName.Length - 1)
            return DiskUsageCategory.Other;
        var ext = fileName[(dot + 1)..];
        return SpanLookup.TryGetValue(ext, out var category) ? category : DiskUsageCategory.Other;
    }

    public static DiskUsageCategory Classify(string fileName) => Classify(fileName.AsSpan());

    public static string Label(DiskUsageCategory category) => category switch
    {
        DiskUsageCategory.Free => Strings.DiskCatFree,
        DiskUsageCategory.Apps => Strings.DiskCatApps,
        DiskUsageCategory.Media => Strings.DiskCatMedia,
        DiskUsageCategory.Archives => Strings.DiskCatArchives,
        DiskUsageCategory.Documents => Strings.DiskCatDocuments,
        DiskUsageCategory.Other => Strings.DiskCatOther,
        DiskUsageCategory.System => Strings.DiskCatSystem,
        _ => Strings.DiskCatOther,
    };

    private static Dictionary<string, DiskUsageCategory> Build(params (DiskUsageCategory Category, string[] Extensions)[] groups)
    {
        var map = new Dictionary<string, DiskUsageCategory>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, extensions) in groups)
        {
            foreach (var ext in extensions)
                map[ext] = category;
        }
        return map;
    }
}

/// <summary>
/// Walks a drive or folder off the UI thread using <see cref="FileSystemEnumerator{T}"/> so each file
/// is classified by extension span with zero per-file string or FileInfo allocations.
/// </summary>
public static class DiskUsageScanner
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>Scans a ready drive and reports periodic snapshots until complete or cancelled.</summary>
    public static DiskUsageSnapshot ScanDrive(DriveFacts drive, Action<DiskUsageSnapshot>? onProgress, CancellationToken ct) =>
        Scan(drive.Name, drive.TotalSize, drive.FreeSpace, onProgress, ct);

    /// <summary>Scans <paramref name="rootPath"/> against known <paramref name="totalBytes"/> and <paramref name="freeBytes"/>.</summary>
    public static DiskUsageSnapshot Scan(
        string rootPath,
        long totalBytes,
        long freeBytes,
        Action<DiskUsageSnapshot>? onProgress,
        CancellationToken ct)
    {
        var safeTotal = Math.Max(0, totalBytes);
        var safeFree = Math.Clamp(freeBytes, 0, safeTotal);

        var bytes = new long[Enum.GetValues<DiskUsageCategory>().Length];
        var counts = new long[bytes.Length];
        bytes[(int)DiskUsageCategory.Free] = safeFree;

        // Emit an initial snapshot right away so the ring shows known free vs used space immediately.
        onProgress?.Invoke(BuildSnapshot(rootPath, safeTotal, safeFree, bytes, counts, 0, 0, 0, isComplete: false));

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            // Skip junctions/symlinks so loops and duplicate mount points aren't double-counted,
            // while keeping Hidden and System files so real disk usage is represented.
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        var clock = Stopwatch.StartNew();
        long scannedFiles = 0;
        long scannedFolders = 0;
        long skippedEntries = 0;
        long tickCounter = 0;

        using var enumerator = new CountingEnumerator(rootPath, options, (ref FileSystemEntry entry) =>
        {
            if ((++tickCounter & 0x1FF) == 0)
            {
                ct.ThrowIfCancellationRequested();
                if (onProgress is not null && clock.Elapsed >= ReportInterval)
                {
                    onProgress(BuildSnapshot(rootPath, safeTotal, safeFree, bytes, counts, scannedFiles, scannedFolders, skippedEntries, isComplete: false));
                    clock.Restart();
                }
            }

            if (entry.IsDirectory)
            {
                scannedFolders++;
                return;
            }

            var category = DiskUsageClassifier.Classify(entry.FileName);
            var idx = (int)category;
            var len = entry.Length;
            if (len > 0)
                bytes[idx] += len;
            counts[idx]++;
            scannedFiles++;
        }, () => skippedEntries++);

        while (enumerator.MoveNext())
        {
            // ShouldIncludeEntry always returns false; the enumerator runs the walk internally.
        }

        ct.ThrowIfCancellationRequested();
        var final = BuildSnapshot(rootPath, safeTotal, safeFree, bytes, counts, scannedFiles, scannedFolders, skippedEntries, isComplete: true);
        onProgress?.Invoke(final);
        return final;
    }

    public static IReadOnlyList<DiskUsageBucket> Sort(IEnumerable<DiskUsageBucket> buckets, DiskUsageSort sort) => sort switch
    {
        DiskUsageSort.Count => [.. buckets
            .OrderByDescending(b => b.FileCount)
            .ThenByDescending(b => b.Bytes)
            .ThenBy(b => b.Category)],
        DiskUsageSort.Name => [.. buckets
            .OrderBy(b => DiskUsageClassifier.Label(b.Category), StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(b => b.Bytes)],
        _ => [.. buckets
            .OrderByDescending(b => b.Bytes)
            .ThenByDescending(b => b.FileCount)
            .ThenBy(b => b.Category)],
    };

    private static DiskUsageSnapshot BuildSnapshot(
        string rootPath,
        long totalBytes,
        long freeBytes,
        long[] bytes,
        long[] counts,
        long scannedFiles,
        long scannedFolders,
        long skippedEntries,
        bool isComplete)
    {
        var scannedBytes =
            bytes[(int)DiskUsageCategory.Apps] +
            bytes[(int)DiskUsageCategory.Media] +
            bytes[(int)DiskUsageCategory.Archives] +
            bytes[(int)DiskUsageCategory.Documents] +
            bytes[(int)DiskUsageCategory.Other];

        var usedBytes = Math.Max(0, totalBytes - freeBytes);
        var remainder = Math.Max(0, usedBytes - scannedBytes);

        var buckets = new List<DiskUsageBucket>(7);
        if (freeBytes > 0 || totalBytes > 0)
            buckets.Add(new(DiskUsageCategory.Free, freeBytes, 0));

        AddScanned(DiskUsageCategory.Apps);
        AddScanned(DiskUsageCategory.Media);
        AddScanned(DiskUsageCategory.Archives);
        AddScanned(DiskUsageCategory.Documents);
        AddScanned(DiskUsageCategory.Other);

        if (remainder > 0)
            buckets.Add(new(DiskUsageCategory.System, remainder, 0));

        return new DiskUsageSnapshot(
            rootPath,
            totalBytes,
            freeBytes,
            scannedBytes,
            scannedFiles,
            scannedFolders,
            skippedEntries,
            isComplete,
            buckets);

        void AddScanned(DiskUsageCategory cat)
        {
            var i = (int)cat;
            buckets.Add(new(cat, bytes[i], counts[i]));
        }
    }

    private delegate void EntryVisitor(ref FileSystemEntry entry);

    private sealed class CountingEnumerator(string directory, EnumerationOptions options, EntryVisitor visit, Action onError)
        : FileSystemEnumerator<byte>(directory, options)
    {
        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry) =>
            entry.IsDirectory && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint);

        protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
        {
            visit(ref entry);
            return false;
        }

        protected override byte TransformEntry(ref FileSystemEntry entry) => 0;

        protected override bool ContinueOnError(int error)
        {
            onError();
            return true;
        }
    }
}
