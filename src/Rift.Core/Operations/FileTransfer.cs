using System.Buffers;
using Rift.Core.Localization;

namespace Rift.Core.Operations;

public enum TransferMode
{
    Copy,
    Move,
}

public sealed record TransferProgress(long BytesDone, long BytesTotal, int ItemsDone, int ItemsTotal, string? CurrentName)
{
    public double Fraction => BytesTotal > 0
        ? Math.Clamp((double)BytesDone / BytesTotal, 0, 1)
        : ItemsTotal > 0 ? Math.Clamp((double)ItemsDone / ItemsTotal, 0, 1) : 0;
}

public sealed class TransferResult
{
    public required OperationJournal Journal { get; init; }
    public required IReadOnlyList<OperationError> Errors { get; init; }

    /// <summary>Where the top-level items ended up; the UI selects them in the destination pane.</summary>
    public required IReadOnlyList<string> Targets { get; init; }

    public bool Canceled { get; init; }
}

/// <summary>
/// Copies or moves files and folders into a folder.
/// Same-volume moves are renames (instant); everything else is a buffered copy with progress,
/// followed by deleting the source for moves. Folders merge into existing ones, file name
/// clashes go to the <see cref="IConflictResolver"/>.
/// </summary>
public sealed class FileTransfer(
    TransferMode mode,
    string destinationDirectory,
    IConflictResolver resolver,
    IProgress<TransferProgress>? progress = null,
    PauseToken pause = default,
    CancellationToken ct = default)
{
    private const int BufferSize = 1 << 20;
    private const int ErrorNotSameDevice = unchecked((int)0x80070011);

    private readonly OperationJournal _journal = new();
    private readonly List<OperationError> _errors = [];
    private readonly List<string> _targets = [];
    private ConflictAction? _always;
    private long _bytesDone;
    private long _bytesTotal;
    private int _itemsDone;
    private int _itemsTotal;
    private long _lastReport;

    /// <summary>Tests use this to exercise the copy+delete path without a second drive.</summary>
    internal bool ForceCrossVolume { get; init; }

    /// <summary>Progress is throttled so the UI isn't flooded; tests set 0.</summary>
    internal int ReportIntervalMs { get; init; } = 100;

    public static Task<TransferResult> RunAsync(
        TransferMode mode,
        IEnumerable<string> sources,
        string destinationDirectory,
        IConflictResolver resolver,
        IProgress<TransferProgress>? progress = null,
        PauseToken pause = default,
        CancellationToken ct = default)
        => new FileTransfer(mode, destinationDirectory, resolver, progress, pause, ct).RunAsync(sources);

    public Task<TransferResult> RunAsync(IEnumerable<string> sources)
    {
        var list = sources.ToList();
        return Task.Run(() => RunCoreAsync(list), CancellationToken.None);
    }

    private async Task<TransferResult> RunCoreAsync(IReadOnlyList<string> sources)
    {
        var destination = Normalize(destinationDirectory);
        var plan = new List<(string Path, bool IsDirectory)>();
        var canceled = false;

        try
        {
            foreach (var source in sources.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                var isDir = Directory.Exists(source);
                if (!isDir && !File.Exists(source))
                {
                    _errors.Add(new OperationError(source, Strings.ErrorFolderNotFound));
                    continue;
                }
                if (isDir && IsSameOrInside(destination, source))
                {
                    _errors.Add(new OperationError(source, Strings.ErrorIntoItself));
                    continue;
                }

                plan.Add((source, isDir));
                Measure(source, isDir, IsFast(source, destination));
            }

            Directory.CreateDirectory(destination);
            Report(null, force: true);

            foreach (var (source, isDir) in plan)
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(source);
                var sameFolder = string.Equals(Path.GetDirectoryName(source), destination, StringComparison.OrdinalIgnoreCase);
                if (sameFolder && mode == TransferMode.Move)
                {
                    _itemsDone++;
                    continue;
                }

                // Copying into the folder it came from always makes a copy next to it, like Explorer.
                var target = Path.Combine(destination, sameFolder ? FileNames.Unique(destination, name, isDir) : name);
                var landed = isDir
                    ? await TransferDirectoryAsync(source, target, IsFast(source, destination))
                    : await TransferFileAsync(source, target, IsFast(source, destination));
                if (landed is not null)
                    _targets.Add(landed);
            }
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        Report(null, force: true);
        return new TransferResult { Journal = _journal, Errors = _errors, Targets = _targets, Canceled = canceled };
    }

    private bool IsFast(string source, string destination) =>
        mode == TransferMode.Move && !ForceCrossVolume && SameVolume(source, destination);

    private void Measure(string source, bool isDir, bool fast)
    {
        if (fast || !isDir)
        {
            _itemsTotal++;
            if (!fast && !isDir)
                _bytesTotal += new FileInfo(source).Length;
            return;
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        foreach (var file in new DirectoryInfo(source).EnumerateFiles("*", options))
        {
            _itemsTotal++;
            _bytesTotal += file.Length;
        }
    }

    /// <returns>The final path of the file, or null if it was skipped or failed.</returns>
    private async Task<string?> TransferFileAsync(string source, string target, bool fast)
    {
        var overwrite = false;
        long size = 0;
        try
        {
            size = new FileInfo(source).Length;
            if (Directory.Exists(target))
            {
                target = KeepBoth(target, isDirectory: false);
            }
            else if (File.Exists(target))
            {
                switch (await ResolveAsync(source, target))
                {
                    case ConflictAction.Skip:
                        _itemsDone++;
                        _bytesDone += fast ? 0 : size;
                        Report(Path.GetFileName(source));
                        return null;
                    case ConflictAction.KeepBoth:
                        target = KeepBoth(target, isDirectory: false);
                        break;
                    case ConflictAction.Replace:
                        overwrite = true;
                        File.SetAttributes(target, FileAttributes.Normal);
                        break;
                    default:
                        throw new OperationCanceledException();
                }
            }

            await pause.WaitWhilePausedAsync(ct);
            if (fast)
            {
                File.Move(source, target, overwrite);
                _journal.AddMoved(source, target);
            }
            else
            {
                await CopyFileAsync(source, target, overwrite);
                if (mode == TransferMode.Move)
                {
                    File.SetAttributes(source, FileAttributes.Normal);
                    File.Delete(source);
                    _journal.AddMoved(source, target);
                }
                else if (!overwrite)
                {
                    _journal.AddCreated(target, isDirectory: false);
                }
            }

            _itemsDone++;
            Report(Path.GetFileName(source));
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _errors.Add(new OperationError(source, ex.Message));
            _itemsDone++;
            if (!fast)
                _bytesDone = Math.Min(_bytesTotal, _bytesDone + size);
            return null;
        }
    }

    private async Task<string?> TransferDirectoryAsync(string source, string target, bool fast)
    {
        try
        {
            if (File.Exists(target))
                target = KeepBoth(target, isDirectory: true);

            if (Directory.Exists(target))
            {
                await MergeAsync(source, target, fast);
                return target;
            }

            if (fast)
            {
                await pause.WaitWhilePausedAsync(ct);
                try
                {
                    Directory.Move(source, target);
                    _journal.AddMoved(source, target);
                    _itemsDone++;
                    Report(Path.GetFileName(source));
                    return target;
                }
                catch (IOException ex) when (ex.HResult == ErrorNotSameDevice)
                {
                    // Same root but different volumes (mount points, subst): fall back to copy+delete.
                    Measure(source, isDir: true, fast: false);
                    fast = false;
                }
            }

            Directory.CreateDirectory(target);
            _journal.AddCreated(target, isDirectory: true);
            await MergeAsync(source, target, fast);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _errors.Add(new OperationError(source, ex.Message));
            return null;
        }
    }

    /// <summary>Moves or copies the contents of <paramref name="source"/> into an existing folder.</summary>
    private async Task MergeAsync(string source, string target, bool fast)
    {
        foreach (var dir in Directory.EnumerateDirectories(source))
            await TransferDirectoryAsync(dir, Path.Combine(target, Path.GetFileName(dir)), fast);
        foreach (var file in Directory.EnumerateFiles(source))
            await TransferFileAsync(file, Path.Combine(target, Path.GetFileName(file)), fast);

        if (mode == TransferMode.Move && !Directory.EnumerateFileSystemEntries(source).Any())
        {
            Directory.Delete(source);
            _journal.AddRemovedEmptyDirectory(source);
        }
    }

    private async Task CopyFileAsync(string source, string target, bool overwrite)
    {
        var info = new FileInfo(source);
        var opened = false;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read,
                             FileShare.Read | FileShare.Delete, 1, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(target, overwrite ? FileMode.Create : FileMode.CreateNew,
                             FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous))
            {
                opened = true;
                if (input.Length > 0)
                    output.SetLength(input.Length);

                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
                {
                    await pause.WaitWhilePausedAsync(ct);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    _bytesDone += read;
                    Report(info.Name);
                }
            }

            File.SetLastWriteTimeUtc(target, info.LastWriteTimeUtc);
            File.SetAttributes(target, info.Attributes);
        }
        catch
        {
            // Never leave a half-written file behind.
            if (opened && File.Exists(target))
            {
                try { File.Delete(target); }
                catch (IOException) { }
            }
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<ConflictAction> ResolveAsync(string source, string target)
    {
        if (_always is { } always)
            return always;

        var s = new FileInfo(source);
        var d = new FileInfo(target);
        var decision = await resolver.ResolveAsync(
            new FileConflict(source, target, s.Length, s.LastWriteTime, d.Length, d.LastWriteTime), ct);
        if (decision.ApplyToAll)
            _always = decision.Action;
        return decision.Action;
    }

    private static string KeepBoth(string target, bool isDirectory)
    {
        var dir = Path.GetDirectoryName(target)!;
        return Path.Combine(dir, FileNames.Unique(dir, Path.GetFileName(target), isDirectory));
    }

    private void Report(string? current, bool force = false)
    {
        if (progress is null)
            return;
        var now = Environment.TickCount64;
        if (!force && now - _lastReport < ReportIntervalMs)
            return;
        _lastReport = now;
        progress.Report(new TransferProgress(_bytesDone, _bytesTotal, _itemsDone, _itemsTotal, current));
    }

    public static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    internal static bool IsSameOrInside(string path, string folder)
    {
        var f = folder.TrimEnd('\\') + '\\';
        var p = path.TrimEnd('\\') + '\\';
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        return full.Length > 3 ? full.TrimEnd('\\') : full;
    }
}
