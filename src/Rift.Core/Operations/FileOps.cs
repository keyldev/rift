using Rift.Core.Localization;

namespace Rift.Core.Operations;

/// <summary>Small single-item operations: rename, new folder, permanent delete.</summary>
public static class FileOps
{
    /// <summary>Renames a file or folder in place. Case-only renames work too.</summary>
    /// <exception cref="ArgumentException">The name is empty or contains invalid characters (message is user-facing).</exception>
    /// <exception cref="IOException">Something with that name already exists.</exception>
    public static (string NewPath, OperationJournal Journal) Rename(string path, string newName)
    {
        var name = FileNames.Clean(newName);
        switch (FileNames.Validate(name))
        {
            case NameProblem.Empty:
                throw new ArgumentException(Strings.RenameEmpty);
            case NameProblem.InvalidCharacters:
                throw new ArgumentException(Strings.RenameInvalidChars);
        }

        var journal = new OperationJournal();
        var parent = Path.GetDirectoryName(path) ?? throw new IOException(Strings.ErrorFolderNotFound);
        var target = Path.Combine(parent, name);
        if (string.Equals(path, target, StringComparison.Ordinal))
            return (path, journal);

        var isDir = Directory.Exists(path);
        var caseOnly = string.Equals(path, target, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && FileNames.Exists(target))
            throw new IOException(Strings.RenameExists(name));

        if (caseOnly)
        {
            // NTFS is case-insensitive: hop through a temporary name.
            var temp = Path.Combine(parent, $"{Guid.NewGuid():N}.rift-tmp");
            Move(path, temp, isDir);
            Move(temp, target, isDir);
        }
        else
        {
            Move(path, target, isDir);
        }

        journal.AddMoved(path, target);
        return (target, journal);
    }

    /// <summary>Creates "New folder", "New folder (2)", … and returns its path.</summary>
    public static (string Path, OperationJournal Journal) CreateFolder(string parent, string baseName)
    {
        var path = System.IO.Path.Combine(parent, FileNames.Unique(parent, baseName, isDirectory: true));
        Directory.CreateDirectory(path);
        var journal = new OperationJournal();
        journal.AddCreated(path, isDirectory: true);
        return (path, journal);
    }

    /// <summary>Deletes for good (Shift+Del). Read-only files are deleted too.</summary>
    public static Task<IReadOnlyList<OperationError>> DeletePermanentlyAsync(
        IEnumerable<string> paths, IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
    {
        var list = paths.ToList();
        return Task.Run<IReadOnlyList<OperationError>>(() =>
        {
            var errors = new List<OperationError>();
            for (var i = 0; i < list.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var path = list[i];
                progress?.Report(new TransferProgress(0, 0, i, list.Count, System.IO.Path.GetFileName(path)));
                try
                {
                    if (Directory.Exists(path))
                        DeleteDirectory(path);
                    else if (File.Exists(path))
                        DeleteFile(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add(new OperationError(path, ex.Message));
                }
            }
            progress?.Report(new TransferProgress(0, 0, list.Count, list.Count, null));
            return errors;
        }, ct);
    }

    private static void DeleteFile(string path)
    {
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
    }

    private static void DeleteDirectory(string path)
    {
        // Don't follow junctions/symlinks into their targets: remove the link itself.
        if (new DirectoryInfo(path).LinkTarget is not null)
        {
            Directory.Delete(path);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path))
            DeleteFile(file);
        foreach (var dir in Directory.EnumerateDirectories(path))
            DeleteDirectory(dir);
        File.SetAttributes(path, FileAttributes.Directory);
        Directory.Delete(path);
    }

    private static void Move(string from, string to, bool isDir)
    {
        if (isDir)
            Directory.Move(from, to);
        else
            File.Move(from, to);
    }
}
