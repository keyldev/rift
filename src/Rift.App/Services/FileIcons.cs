using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Rift.App.ViewModels;
using Rift.Shell;
using Rift.Core;

namespace Rift.App.Services;

/// <summary>
/// System icons for file rows, loaded off the UI thread when a row first appears and cached by
/// file type, so a folder of 10,000 photos costs one shell call. Rows show Rift's line icon until then.
/// </summary>
public static class FileIcons
{
    /// <summary>Per-file icons (apps, shortcuts) are dropped past this; per-type ones are few anyway.</summary>
    private const int MaxCached = 4000;

    // UI thread only.
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<FileItem>> Waiting = new(StringComparer.OrdinalIgnoreCase);

    [SupportedOSPlatform("windows")]
    private static readonly Lazy<StaWorker> Worker = new(() => new StaWorker("Rift icons"));

    public static void Request(FileItem item)
    {
        if (!OperatingSystem.IsWindows() || item.Icon is not null)
            return;
        Load(item);
    }

    [SupportedOSPlatform("windows")]
    private static void Load(FileItem item)
    {
        var entry = item.Entry;
        var key = ShellIcons.KeyFor(entry.FullPath, entry.IsDirectory, entry.Attributes);
        if (Cache.TryGetValue(key, out var cached))
        {
            item.Icon = cached;
            return;
        }
        if (Waiting.TryGetValue(key, out var waiting))
        {
            waiting.Add(item);
            return;
        }

        Waiting[key] = [item];
        Worker.Value.Invoke(() => ShellIcons.Load(entry.FullPath, entry.IsDirectory, entry.Attributes))
            .ContinueWith(t => Dispatcher.UIThread.Post(() => Deliver(key, t)), TaskScheduler.Default);
    }

    private static void Deliver(string key, Task<IconPixels?> load)
    {
        Bitmap? bitmap = null;
        if (load.IsCompletedSuccessfully && load.Result is { } pixels)
            bitmap = ToBitmap(pixels);
        else if (load.Exception is { } ex)
            Log.Warn($"Icon for {key} failed", ex.InnerException ?? ex);

        if (Cache.Count >= MaxCached)
            Cache.Clear();
        Cache[key] = bitmap;
        if (Waiting.Remove(key, out var items))
        {
            foreach (var item in items)
                item.Icon = bitmap;
        }
    }

    private static Bitmap ToBitmap(IconPixels pixels)
    {
        var handle = GCHandle.Alloc(pixels.Bgra, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, handle.AddrOfPinnedObject(),
                new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), pixels.Width * 4);
        }
        finally
        {
            handle.Free();
        }
    }
}
