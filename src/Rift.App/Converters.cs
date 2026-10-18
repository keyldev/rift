using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Rift.App.ViewModels;
using Rift.Core;

namespace Rift.App;

public static class Converters
{
    // Icon colors per file kind; the same in both themes, as in the mockup.
    private static readonly Dictionary<FileKind, IBrush> KindBrushes = new()
    {
        [FileKind.Folder] = Brush("#E8B04A"),
        [FileKind.Image] = Brush("#4FA8E8"),
        [FileKind.Video] = Brush("#B57CFF"),
        [FileKind.Audio] = Brush("#EC6FA0"),
        [FileKind.Archive] = Brush("#EE9A50"),
        [FileKind.Code] = Brush("#4CC28A"),
        [FileKind.Document] = Brush("#8E9BAD"),
        [FileKind.Pdf] = Brush("#EC5F5F"),
        [FileKind.Executable] = Brush("#7F8EFF"),
        [FileKind.Other] = Brush("#8E9BAD"),
    };

    private static readonly IBrush FolderFill = Brush("#40E8B04A");

    public static readonly IValueConverter KindToBrush =
        new FuncValueConverter<FileKind, IBrush>(k => KindBrushes[k]);

    public static readonly IValueConverter KindToFill =
        new FuncValueConverter<FileKind, IBrush?>(k => k == FileKind.Folder ? FolderFill : null);

    public static readonly IValueConverter KindToIcon =
        new FuncValueConverter<FileKind, Geometry?>(k => Resource(k == FileKind.Folder ? "Icon.Folder" : "Icon.File"));

    public static readonly IValueConverter ResourceToGeometry =
        new FuncValueConverter<string, Geometry?>(key => key is null ? null : Resource(key));

    public static readonly IValueConverter SplitToSpan =
        new FuncValueConverter<bool, int>(split => split ? 1 : 3);

    public static readonly IValueConverter ThemeIcon =
        new FuncValueConverter<bool, Geometry?>(dark => Resource(dark ? "Icon.Sun" : "Icon.Moon"));

    public static readonly IValueConverter OperationIcon =
        new FuncValueConverter<OperationState, Geometry?>(s => Resource(s switch
        {
            OperationState.Done => "Icon.Check",
            OperationState.Failed => "Icon.Alert",
            OperationState.Canceled => "Icon.Close",
            _ => "Icon.Copy",
        }));

    public static readonly IValueConverter DropIcon =
        new FuncValueConverter<bool, Geometry?>(copy => Resource(copy ? "Icon.Copy" : "Icon.Move"));

    public static readonly IValueConverter FractionToPercent =
        new FuncValueConverter<double?, double>(f => (f ?? 0) * 100);

    private static Geometry? Resource(string key) =>
        Application.Current?.TryFindResource(key, out var value) == true ? value as Geometry : null;

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
