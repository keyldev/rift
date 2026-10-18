namespace Rift.Core;

public enum FileKind
{
    Folder,
    Image,
    Video,
    Audio,
    Archive,
    Code,
    Document,
    Pdf,
    Executable,
    Other,
}

/// <param name="IsHidden">Hidden or system: listed only when "show hidden files" is on, and drawn dimmed.</param>
/// <param name="DateNote">Replaces the Modified column (e.g. a drive's free space).</param>
/// <param name="TypeNote">Replaces the Type column (e.g. where a deleted file came from).</param>
public sealed record FileEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long? Size,
    DateTime Modified,
    FileKind Kind,
    bool IsHidden,
    FileAttributes Attributes = 0,
    string? DateNote = null,
    string? TypeNote = null,
    DateTime Created = default)
{
    public static FileEntry From(FileSystemInfo info)
    {
        var isDir = info is DirectoryInfo;
        return new FileEntry(
            info.Name,
            info.FullName,
            isDir,
            isDir ? null : ((FileInfo)info).Length,
            info.LastWriteTime,
            isDir ? FileKind.Folder : FileKinds.FromExtension(info.Extension),
            (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0,
            info.Attributes,
            Created: info.CreationTime);
    }
}
