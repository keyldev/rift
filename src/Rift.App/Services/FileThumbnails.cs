using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Rift.App.ViewModels;
using Rift.Shell;

namespace Rift.App.Services;

/// <summary>
/// Thumbnails for the tiles and thumbnail views. Rows ask when they come on screen; the newest request
/// is served first, and requests for rows that scrolled away are dropped, so fast scrolling stays cheap.
/// </summary>
public static class FileThumbnails
{
    private const int MaxCached = 600;
    private const int Workers = 2;

    private sealed record Job(FileItem Item, string Key, int Size);

    // UI thread only.
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> Age = new();
    private static readonly Dictionary<FileItem, Job> Wanted = [];

    // Shared with the workers.
    private static readonly Stack<Job> Pending = new();
    private static readonly Lock PendingLock = new();
    private static readonly SemaphoreSlim Signal = new(0);
    private static bool _started;

    /// <summary>Asks for a thumbnail of <paramref name="size"/> device pixels; it lands in <see cref="FileItem.Thumbnail"/>.</summary>
    public static void Request(FileItem item, int size)
    {
        if (!OperatingSystem.IsWindows())
            return;
        var key = $"{item.FullPath}|{size}|{item.Entry.Modified.Ticks}";
        if (Cache.TryGetValue(key, out var cached))
        {
            item.Thumbnail = cached;
            return;
        }

        var request = new Job(item, key, size);
        Wanted[item] = request;
        lock (PendingLock)
            Pending.Push(request);
        Signal.Release();
        Start();
    }

    /// <summary>The row left the screen: don't spend time on it.</summary>
    public static void Forget(FileItem item) => Wanted.Remove(item);

    [SupportedOSPlatform("windows")]
    private static void Start()
    {
        if (_started)
            return;
        _started = true;
        for (var i = 0; i < Workers; i++)
        {
            var thread = new Thread(Work) { IsBackground = true, Name = $"Rift thumbnails {i + 1}" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }

    [SupportedOSPlatform("windows")]
    private static void Work()
    {
        while (true)
        {
            Signal.Wait();
            Job? request;
            lock (PendingLock)
                Pending.TryPop(out request);
            if (request is null)
                continue;

            // Cheap check without the UI thread: skip if a newer request for the same row replaced this one.
            var stillWanted = Dispatcher.UIThread.Invoke(() => Wanted.TryGetValue(request.Item, out var current) && current == request);
            if (!stillWanted)
                continue;

            IconPixels? pixels = null;
            try
            {
                pixels = ShellThumbnails.Load(request.Item.FullPath, request.Size);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Thumbnail for {request.Item.FullPath} failed: {ex.Message}");
            }
            Dispatcher.UIThread.Post(() => Deliver(request, pixels));
        }
    }

    private static void Deliver(Job request, IconPixels? pixels)
    {
        var bitmap = pixels is null ? null : ToBitmap(pixels);
        Cache[request.Key] = bitmap;
        Age.AddLast(request.Key);
        while (Age.Count > MaxCached && Age.First is { } oldest)
        {
            Cache.Remove(oldest.Value);
            Age.RemoveFirst();
        }

        if (Wanted.TryGetValue(request.Item, out var current) && current == request)
        {
            Wanted.Remove(request.Item);
            request.Item.Thumbnail = bitmap;
        }
    }

    private static Bitmap ToBitmap(IconPixels pixels)
    {
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
