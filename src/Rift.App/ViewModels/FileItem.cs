using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Rift.Core;
using Rift.Core.Localization;

namespace Rift.App.ViewModels;

/// <summary>A row of the file list with display strings computed once.</summary>
public sealed partial class FileItem(FileEntry entry) : ObservableObject
{
    public FileEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string FullPath => Entry.FullPath;
    public FileKind Kind => Entry.Kind;
    public bool IsDirectory => Entry.IsDirectory;
    public string SizeText { get; } = SizeFormatter.Format(entry.Size);
    public string DateText { get; } = entry.Modified.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
    public string KindText { get; } = KindLabel(entry);

    /// <summary>F2: the name turns into a text box.</summary>
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private string _editName = "";

    private static string KindLabel(FileEntry e) => e.Kind switch
    {
        FileKind.Folder => Strings.KindFolder,
        FileKind.Image => Strings.KindImage,
        FileKind.Video => Strings.KindVideo,
        FileKind.Audio => Strings.KindAudio,
        FileKind.Archive => Strings.KindArchive,
        FileKind.Code => Strings.KindCode,
        FileKind.Document => Strings.KindDocument,
        FileKind.Pdf => Strings.KindPdf,
        FileKind.Executable => Strings.KindExecutable,
        _ => Path.GetExtension(e.Name) is { Length: > 1 } ext ? ext[1..].ToUpperInvariant() : Strings.KindFile,
    };
}
