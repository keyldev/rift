using System.Globalization;
using Rift.Core;

namespace Rift.App.ViewModels;

/// <summary>A row of the file list with display strings computed once.</summary>
public sealed class FileItem(FileEntry entry)
{
    public FileEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public FileKind Kind => Entry.Kind;
    public bool IsDirectory => Entry.IsDirectory;
    public string SizeText { get; } = SizeFormatter.Format(entry.Size);
    public string DateText { get; } = entry.Modified.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
    public string KindText { get; } = KindLabel(entry);

    private static string KindLabel(FileEntry e) => e.Kind switch
    {
        FileKind.Folder => "Папка",
        FileKind.Image => "Изображение",
        FileKind.Video => "Видео",
        FileKind.Audio => "Аудио",
        FileKind.Archive => "Архив",
        FileKind.Code => "Исходник",
        FileKind.Document => "Документ",
        FileKind.Pdf => "PDF",
        FileKind.Executable => "Приложение",
        _ => Path.GetExtension(e.Name) is { Length: > 1 } ext ? ext[1..].ToUpperInvariant() : "Файл",
    };
}
