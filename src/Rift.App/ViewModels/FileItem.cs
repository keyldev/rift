using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Rift.Core;
using Rift.Core.Localization;

namespace Rift.App.ViewModels;

/// <summary>A row of the file list with display strings computed once per change on disk.</summary>
public sealed partial class FileItem : ObservableObject
{
    public FileItem(FileEntry entry)
    {
        _entry = entry;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(FullPath), nameof(Kind), nameof(IsDirectory), nameof(IsHidden),
        nameof(SizeText), nameof(DateText), nameof(KindText))]
    private FileEntry _entry;

    public string Name => Entry.Name;
    public string FullPath => Entry.FullPath;
    public FileKind Kind => Entry.Kind;
    public bool IsDirectory => Entry.IsDirectory;
    public bool IsHidden => Entry.IsHidden;
    public string SizeText => SizeFormatter.Format(Entry.Size);
    public string DateText => Entry.DateNote ?? Entry.Modified.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
    public string KindText => Entry.TypeNote ?? KindLabel(Entry);

    /// <summary>The system icon, once loaded (see Services.FileIcons); until then the row shows a line icon.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private IImage? _icon;

    public bool HasIcon => Icon is not null;

    /// <summary>F2: the name turns into a text box.</summary>
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private string _editName = "";

    /// <summary>Called when the folder reloads and this file is still there; a no-op if nothing changed.</summary>
    public void Update(FileEntry entry)
    {
        if (entry != Entry)
            Entry = entry;
    }

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
