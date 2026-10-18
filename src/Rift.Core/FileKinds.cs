namespace Rift.Core;

public static class FileKinds
{
    private static readonly Dictionary<string, FileKind> ByExtension = Build(
        (FileKind.Image, ["png", "jpg", "jpeg", "gif", "webp", "bmp", "svg", "ico", "heic", "tiff", "avif"]),
        (FileKind.Video, ["mp4", "mkv", "avi", "mov", "webm", "wmv", "m4v"]),
        (FileKind.Audio, ["mp3", "flac", "wav", "ogg", "m4a", "aac", "opus"]),
        (FileKind.Archive, ["zip", "7z", "rar", "tar", "gz", "xz", "bz2", "zst", "iso"]),
        (FileKind.Code, ["cs", "csproj", "sln", "slnx", "props", "targets", "axaml", "xaml", "js", "ts", "jsx", "tsx",
            "json", "xml", "yml", "yaml", "toml", "py", "rs", "go", "c", "cpp", "h", "hpp", "java", "kt", "sh", "ps1",
            "html", "css", "scss", "sql"]),
        (FileKind.Document, ["txt", "md", "log", "ini", "cfg", "doc", "docx", "rtf", "odt", "xls", "xlsx", "csv", "ppt", "pptx"]),
        (FileKind.Pdf, ["pdf"]),
        (FileKind.Executable, ["exe", "msi", "bat", "cmd", "lnk", "appx", "msix"]));

    public static FileKind FromExtension(string extension)
    {
        var ext = extension.TrimStart('.');
        return ByExtension.TryGetValue(ext, out var kind) ? kind : FileKind.Other;
    }

    private static Dictionary<string, FileKind> Build(params (FileKind Kind, string[] Extensions)[] groups)
    {
        var map = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, extensions) in groups)
        {
            foreach (var ext in extensions)
                map[ext] = kind;
        }
        return map;
    }
}
