using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using Rift.Core;
using Rift.Core.Localization;
using Rift.Shell;

namespace Rift.App.ViewModels;

public sealed record PreviewLine(string Label, string Value);

/// <summary>
/// The preview pane (Alt+P): a picture (the image itself, or the shell's thumbnail for video, PDF, documents),
/// the start of a text file, and the file's facts underneath.
/// </summary>
public sealed partial class PreviewViewModel : ObservableObject
{
    /// <summary>Text preview reads this much; enough to recognize a file, cheap on a slow disk.</summary>
    private const int TextBytes = 48 * 1024;
    private const int ImagePixels = 1200;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".ini", ".cfg", ".conf", ".csv", ".tsv", ".json", ".xml", ".yml", ".yaml", ".toml",
        ".cs", ".csproj", ".sln", ".slnx", ".props", ".targets", ".axaml", ".xaml", ".js", ".ts", ".jsx", ".tsx", ".py",
        ".rs", ".go", ".c", ".cpp", ".h", ".hpp", ".java", ".kt", ".sh", ".ps1", ".bat", ".cmd", ".html", ".css",
        ".scss", ".sql", ".gitignore", ".gitattributes", ".editorconfig", ".env", ".reg",
    };

    // Formats Avalonia decodes itself, at full quality; the rest go through the shell.
    private static readonly HashSet<string> DecodableImages = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico",
    };

    private static StaWorker? _worker;
    private CancellationTokenSource? _loading;

    [ObservableProperty] private string? _title;
    [ObservableProperty] private IImage? _image;
    [ObservableProperty] private string? _text;
    [ObservableProperty] private IReadOnlyList<PreviewLine> _lines = [];
    [ObservableProperty] private bool _isEmpty = true;

    public bool HasImage => Image is not null;
    public bool HasText => Text is not null;

    partial void OnImageChanged(IImage? value) => OnPropertyChanged(nameof(HasImage));
    partial void OnTextChanged(string? value) => OnPropertyChanged(nameof(HasText));

    /// <summary>Shows <paramref name="item"/>, or the empty state for null. Loading happens off the UI thread.</summary>
    public async Task ShowAsync(FileItem? item, double renderScaling)
    {
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();

        if (item is null)
        {
            (Title, Image, Text, Lines, IsEmpty) = (null, null, null, [], true);
            return;
        }

        var entry = item.Entry;
        IsEmpty = false;
        Title = entry.Name;
        Text = null;
        Image = item.Thumbnail; // something right away when the grid already has it; the real picture follows
        Lines = Facts(entry, null);

        try
        {
            // Wait a moment: arrowing through a folder shouldn't decode every file on the way.
            await Task.Delay(120, cts.Token);
            var ext = Path.GetExtension(entry.Name);

            if (!entry.IsDirectory && TextExtensions.Contains(ext) && await Task.Run(() => ReadText(entry.FullPath), cts.Token) is { } text)
            {
                Text = text;
                Image = null;
                return;
            }

            if (!entry.IsDirectory && DecodableImages.Contains(ext))
            {
                var (bitmap, size) = await Task.Run(() => DecodeImage(entry.FullPath), cts.Token);
                if (cts.IsCancellationRequested)
                    return;
                if (bitmap is not null)
                {
                    Image = bitmap;
                    Lines = Facts(entry, size);
                    return;
                }
            }

            if (OperatingSystem.IsWindows())
            {
                // A real thumbnail fills the pane; without one, a crisp 128 px icon beats a blown-up one.
                var shell = await ShellImageAsync(entry.FullPath, (int)(320 * renderScaling), thumbnailOnly: true)
                            ?? await ShellImageAsync(entry.FullPath, (int)(128 * renderScaling), thumbnailOnly: false);
                if (!cts.IsCancellationRequested && shell is not null)
                    Image = shell;
            }

            if (entry.IsDirectory)
            {
                var count = await Task.Run(() => CountItems(entry.FullPath), cts.Token);
                if (!cts.IsCancellationRequested && count is { } n)
                    Lines = [.. Facts(entry, null), new PreviewLine(Strings.PreviewContains, Strings.ItemsCount(n))];
            }
        }
        catch (OperationCanceledException)
        {
            // Selection moved on.
        }
    }

    private static IReadOnlyList<PreviewLine> Facts(FileEntry e, (int Width, int Height)? size)
    {
        var lines = new List<PreviewLine> { new(Strings.ColumnType, FileKinds.Label(e)) };
        if (e.Size is { } bytes)
            lines.Add(new(Strings.ColumnSize, $"{SizeFormatter.Format(bytes)} ({bytes.ToString("N0", CultureInfo.CurrentCulture)} {Strings.SizeB})"));
        if (size is { } s)
            lines.Add(new(Strings.PreviewDimensions, $"{s.Width} × {s.Height}"));
        if (e.DateNote is null && e.Modified != default)
            lines.Add(new(Strings.ColumnModified, e.Modified.ToString("g", CultureInfo.CurrentCulture)));
        if (e.Created != default)
            lines.Add(new(Strings.ColumnCreated, e.Created.ToString("g", CultureInfo.CurrentCulture)));
        if (Path.GetDirectoryName(e.FullPath) is { } folder && !Locations.IsVirtual(folder))
            lines.Add(new(Strings.ColumnLocation, folder));
        return lines;
    }

    /// <summary>The start of a text file, or null if it looks binary.</summary>
    private static string? ReadText(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[Math.Min(TextBytes, stream.Length)];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (Array.IndexOf(buffer, (byte)0, 0, read) >= 0 && !HasUnicodeBom(buffer))
                return null;
            using var reader = new StreamReader(new MemoryStream(buffer, 0, read), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();
            return stream.Length > read ? text + "\n…" : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool HasUnicodeBom(byte[] b) => b.Length >= 2 && ((b[0] == 0xFF && b[1] == 0xFE) || (b[0] == 0xFE && b[1] == 0xFF));

    private static (Bitmap?, (int, int)?) DecodeImage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var size = ImageInfo.TryGetSize(stream);
            stream.Position = 0;
            // Big photos are decoded straight to screen size: a 50-megapixel file shouldn't cost 200 MB.
            var bitmap = size is { Width: > ImagePixels } ? Bitmap.DecodeToWidth(stream, ImagePixels) : new Bitmap(stream);
            return (bitmap, size ?? (bitmap.PixelSize.Width, bitmap.PixelSize.Height));
        }
        catch (Exception)
        {
            // Unsupported variant or a broken file; the shell may still manage a thumbnail.
            return (null, null);
        }
    }

    private static int? CountItems(string folder)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(folder).Take(100_000).Count();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task<Bitmap?> ShellImageAsync(string path, int size, bool thumbnailOnly)
    {
        _worker ??= new StaWorker("Rift preview");
        var pixels = await _worker.Invoke(() => ShellThumbnails.Load(path, size, thumbnailOnly));
        if (pixels is null)
            return null;
        var handle = GCHandle.Alloc(pixels.Bgra, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, handle.AddrOfPinnedObject(),
                new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), pixels.Width * 4);
        }
        finally
        {
            handle.Free();
        }
    }
}
