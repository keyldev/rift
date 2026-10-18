namespace Rift.Core;

/// <summary>
/// Watches one folder (not its subfolders) and raises <see cref="Changed"/> once a burst of changes settles.
/// A copy of 500 files gives a handful of reloads, not 500.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    /// <summary>Quiet time after the last change before <see cref="Changed"/> fires.</summary>
    public static TimeSpan Settle { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>During a long burst, fire at least this often so the user sees progress.</summary>
    public static TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(1);

    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _timer;
    private readonly Lock _lock = new();
    private DateTime _firstPending;
    private bool _pending;
    private bool _disposed;

    /// <summary>Raised on a thread-pool thread.</summary>
    public event Action? Changed;

    public FolderWatcher(string path)
    {
        _timer = new Timer(_ => Fire());
        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size |
                               NotifyFilters.LastWrite | NotifyFilters.Attributes,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Created += OnEvent;
            _watcher.Deleted += OnEvent;
            _watcher.Changed += OnEvent;
            _watcher.Renamed += OnEvent;
            // Buffer overflow or the folder itself went away: reload and let the reader sort it out.
            _watcher.Error += (_, _) => Schedule();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Some locations (certain network shares, virtual drives) can't be watched; Ctrl+R still works.
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    public bool IsWatching => _watcher is not null;

    private void OnEvent(object sender, FileSystemEventArgs e) => Schedule();

    private void Schedule()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            var now = DateTime.UtcNow;
            if (!_pending)
            {
                _pending = true;
                _firstPending = now;
            }
            var untilMax = _firstPending + MaxDelay - now;
            var delay = untilMax < Settle ? untilMax : Settle;
            _timer.Change(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        lock (_lock)
        {
            if (_disposed || !_pending)
                return;
            _pending = false;
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        _watcher?.Dispose();
        _timer.Dispose();
    }
}
