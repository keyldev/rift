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

public sealed record FileEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long? Size,
    DateTime Modified,
    FileKind Kind,
    bool IsHidden)
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
            info.Attributes.HasFlag(FileAttributes.Hidden));
    }
}
