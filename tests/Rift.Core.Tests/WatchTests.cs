namespace Rift.Core.Tests;

public class SortedSyncTests
{
    private sealed class Row(string key, int version)
    {
        public string Key { get; } = key;
        public int Version { get; set; } = version;
    }

    private static List<Row> Rows(params string[] keys) => keys.Select(k => new Row(k, 0)).ToList();

    private static void Sync(List<Row> target, params (string Key, int Version)[] source) =>
        SortedSync.Apply(target, source, r => r.Key, e => e.Key, e => new Row(e.Key, e.Version), (r, e) => r.Version = e.Version);

    [Fact]
    public void Keeps_unchanged_items_and_edits_in_place()
    {
        var target = Rows("a", "c", "d");
        var c = target[1];

        Sync(target, ("a", 0), ("b", 0), ("c", 5), ("e", 0));

        Assert.Equal(["a", "b", "c", "e"], target.Select(r => r.Key));
        Assert.Same(c, target[2]);
        Assert.Equal(5, c.Version);
    }

    [Fact]
    public void Empties_and_fills()
    {
        var target = Rows("a", "b");
        Sync(target);
        Assert.Empty(target);

        Sync(target, ("x", 0), ("y", 0));
        Assert.Equal(["x", "y"], target.Select(r => r.Key));
    }

    [Fact]
    public void Rebuilds_when_order_changed()
    {
        var target = Rows("a", "b", "c");
        Sync(target, ("c", 0), ("a", 0), ("b", 0));
        Assert.Equal(["c", "a", "b"], target.Select(r => r.Key));
    }

    [Fact]
    public void Recreates_items_that_changed_slot()
    {
        var target = Rows("a", "b");
        var b = target[1];
        SortedSync.Apply(target, [("a", 0), ("b", 1)], r => r.Key, e => e.Item1, e => new Row(e.Item1, e.Item2),
            (r, e) => r.Version = e.Item2, sameSlot: (r, e) => r.Version == e.Item2);
        Assert.NotSame(b, target[1]);
        Assert.Equal(1, target[1].Version);
    }

    [Fact]
    public void Gives_up_untouched_when_there_are_too_many_edits()
    {
        var target = Rows("a", "b", "c", "d");
        var done = SortedSync.Apply(target, [("a", 0), ("x", 0), ("y", 0)], r => r.Key, e => e.Item1, e => new Row(e.Item1, e.Item2),
            (r, e) => r.Version = e.Item2, maxEdits: 4);

        Assert.False(done); // 3 removed + 2 added
        Assert.Equal(["a", "b", "c", "d"], target.Select(r => r.Key));
    }

    [Fact]
    public void Same_files_only_update()
    {
        var target = Rows("a", "B", "c");
        var rows = target.ToList();
        var done = SortedSync.Apply(target, [("A", 1), ("b", 2), ("c", 3)], r => r.Key, e => e.Item1, e => new Row(e.Item1, e.Item2),
            (r, e) => r.Version = e.Item2, maxEdits: 0);

        Assert.True(done);
        Assert.Equal(rows, target);
        Assert.Equal([1, 2, 3], target.Select(r => r.Version));
    }
}

public class FolderWatcherTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rift-watch-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Reports_a_burst_of_changes()
    {
        using var watcher = new FolderWatcher(_root);
        Assert.True(watcher.IsWatching);
        var count = 0;
        var fired = new TaskCompletionSource();
        watcher.Changed += () =>
        {
            Interlocked.Increment(ref count);
            fired.TrySetResult();
        };

        for (var i = 0; i < 20; i++)
            File.WriteAllText(Path.Combine(_root, $"f{i}.txt"), "x");

        await fired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(FolderWatcher.MaxDelay + FolderWatcher.Settle);
        // Twenty files settle into a couple of reloads, not twenty.
        Assert.InRange(count, 1, 3);
    }

    [Fact]
    public void Missing_folder_is_not_watched()
    {
        using var watcher = new FolderWatcher(Path.Combine(_root, "nope"));
        Assert.False(watcher.IsWatching);
    }
}

public class HiddenFilesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rift-hidden-").FullName;

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_root))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Hidden_and_system_files_only_on_request()
    {
        File.WriteAllText(Path.Combine(_root, "plain.txt"), "x");
        var hidden = Path.Combine(_root, "hidden.txt");
        var system = Path.Combine(_root, "system.txt");
        File.WriteAllText(hidden, "x");
        File.WriteAllText(system, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.SetAttributes(system, FileAttributes.System);

        Assert.Equal(["plain.txt"], DirectoryReader.Read(_root).Select(e => e.Name));

        var all = DirectoryReader.Read(_root, includeHidden: true);
        Assert.Equal(["hidden.txt", "plain.txt", "system.txt"], all.Select(e => e.Name));
        Assert.Equal([true, false, true], all.Select(e => e.IsHidden));
    }
}

public class DriveReaderTests
{
    [Fact]
    public void A_drive_that_hangs_does_not_hold_up_the_rest()
    {
        var fast = new DriveInfo("C");
        var hanging = new DriveInfo("C");
        using var release = new ManualResetEventSlim();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var drives = DriveReader.Read([fast, hanging], TimeSpan.FromMilliseconds(200), d =>
        {
            if (ReferenceEquals(d, hanging))
                release.Wait();
            return new DriveFacts(d.Name, DriveType.Fixed, true, "Disk", 100, 40);
        });
        release.Set();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
        Assert.True(drives[0] is { IsReady: true, IsSlow: false, FreeSpace: 40 });
        Assert.True(drives[1] is { IsReady: false, IsSlow: true, TotalSize: 0 });
        Assert.Equal("C:", drives[1].Letter);
    }
}
