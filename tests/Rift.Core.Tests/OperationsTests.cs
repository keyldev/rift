using Rift.Core.Operations;

namespace Rift.Core.Tests;

public abstract class TempFolderTest : IDisposable
{
    protected readonly string Root = Directory.CreateTempSubdirectory("rift-ops-").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Root, recursive: true);
    }

    protected string P(params string[] parts) => Path.Combine([Root, .. parts]);

    protected string Write(string relative, string content = "x", DateTime? modified = null)
    {
        var path = P(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (modified is { } m)
            File.SetLastWriteTime(path, m);
        return path;
    }

    protected string Dir(string relative) => Directory.CreateDirectory(P(relative.Split('/'))).FullName;

    protected static string Read(string path) => File.ReadAllText(path);

    protected static Task<TransferResult> Run(TransferMode mode, string dest, ConflictAction onConflict, params string[] sources) =>
        FileTransfer.RunAsync(mode, sources, dest, new FixedConflictResolver(onConflict));
}

public class FileTransferTests : TempFolderTest
{
    [Fact]
    public async Task Copies_a_tree_keeping_timestamps()
    {
        var when = new DateTime(2024, 5, 1, 12, 0, 0);
        Write("src/a.txt", "alpha", when);
        Write("src/sub/b.txt", "beta");
        Dir("src/empty");
        var dest = Dir("dest");

        var result = await Run(TransferMode.Copy, dest, ConflictAction.Skip, P("src"));

        Assert.Empty(result.Errors);
        Assert.Equal([P("dest", "src")], result.Targets);
        Assert.Equal("alpha", Read(P("dest", "src", "a.txt")));
        Assert.Equal("beta", Read(P("dest", "src", "sub", "b.txt")));
        Assert.True(Directory.Exists(P("dest", "src", "empty")));
        Assert.Equal(when, File.GetLastWriteTime(P("dest", "src", "a.txt")));
        Assert.True(File.Exists(P("src", "a.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Moves_files_and_folders(bool crossVolume)
    {
        Write("src/a.txt", "alpha");
        Write("src/dir/b.txt", "beta");
        var dest = Dir("dest");

        var transfer = new FileTransfer(TransferMode.Move, dest, new FixedConflictResolver(ConflictAction.Skip))
        {
            ForceCrossVolume = crossVolume,
        };
        var result = await transfer.RunAsync([P("src", "a.txt"), P("src", "dir")]);

        Assert.Empty(result.Errors);
        Assert.Equal("alpha", Read(P("dest", "a.txt")));
        Assert.Equal("beta", Read(P("dest", "dir", "b.txt")));
        Assert.False(File.Exists(P("src", "a.txt")));
        Assert.False(Directory.Exists(P("src", "dir")));
    }

    [Theory]
    [InlineData(ConflictAction.Replace, "new", null)]
    [InlineData(ConflictAction.Skip, "old", null)]
    [InlineData(ConflictAction.KeepBoth, "old", "new")]
    public async Task Resolves_file_conflicts(ConflictAction action, string expected, string? copy)
    {
        var source = Write("src/a.txt", "new");
        Write("dest/a.txt", "old");

        var result = await Run(TransferMode.Copy, P("dest"), action, source);

        Assert.Empty(result.Errors);
        Assert.Equal(expected, Read(P("dest", "a.txt")));
        Assert.Equal(copy, File.Exists(P("dest", "a (2).txt")) ? Read(P("dest", "a (2).txt")) : null);
    }

    [Fact]
    public async Task Apply_to_all_asks_only_once()
    {
        Write("src/a.txt", "1");
        Write("src/b.txt", "2");
        Write("dest/a.txt", "old");
        Write("dest/b.txt", "old");
        var resolver = new CountingResolver(new ConflictDecision(ConflictAction.Replace, ApplyToAll: true));

        await FileTransfer.RunAsync(TransferMode.Copy, [P("src", "a.txt"), P("src", "b.txt")], P("dest"), resolver);

        Assert.Equal(1, resolver.Calls);
        Assert.Equal("2", Read(P("dest", "b.txt")));
    }

    [Fact]
    public async Task Cancel_in_conflict_stops_and_reports_canceled()
    {
        var source = Write("src/a.txt", "new");
        Write("dest/a.txt", "old");

        var result = await Run(TransferMode.Copy, P("dest"), ConflictAction.Cancel, source);

        Assert.True(result.Canceled);
        Assert.Equal("old", Read(P("dest", "a.txt")));
    }

    [Fact]
    public async Task Merges_folder_into_existing_one()
    {
        Write("src/photos/1.jpg", "one");
        Write("dest/photos/2.jpg", "two");

        var result = await Run(TransferMode.Move, P("dest"), ConflictAction.Skip, P("src", "photos"));

        Assert.Empty(result.Errors);
        Assert.Equal("one", Read(P("dest", "photos", "1.jpg")));
        Assert.Equal("two", Read(P("dest", "photos", "2.jpg")));
        Assert.False(Directory.Exists(P("src", "photos")));
    }

    [Fact]
    public async Task Copy_into_the_same_folder_makes_a_numbered_copy()
    {
        var source = Write("dir/report.pdf", "r");

        var result = await Run(TransferMode.Copy, P("dir"), ConflictAction.Cancel, source);

        Assert.Equal([P("dir", "report (2).pdf")], result.Targets);
        Assert.Equal("r", Read(P("dir", "report (2).pdf")));
    }

    [Fact]
    public async Task Refuses_to_put_a_folder_inside_itself()
    {
        Dir("a/b");

        var result = await Run(TransferMode.Copy, P("a", "b"), ConflictAction.Skip, P("a"));

        Assert.Single(result.Errors);
        Assert.False(Directory.Exists(P("a", "b", "a")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_removes_the_partial_file(bool streamCopy)
    {
        var big = P("big.bin");
        await File.WriteAllBytesAsync(big, new byte[8 << 20]);
        Dir("dest");
        using var cts = new CancellationTokenSource();
        var progress = new CallbackProgress(p =>
        {
            if (p.BytesDone > 0)
                cts.Cancel();
        });

        var transfer = new FileTransfer(TransferMode.Copy, P("dest"), new FixedConflictResolver(ConflictAction.Skip),
            progress, ct: cts.Token) { ReportIntervalMs = 0, UseStreamCopy = streamCopy };
        var result = await transfer.RunAsync([big]);

        Assert.True(result.Canceled);
        Assert.False(File.Exists(P("dest", "big.bin")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Copies_keep_attributes_and_replace_read_only_files(bool streamCopy)
    {
        var source = Write("src/a.txt", "new", new DateTime(2023, 1, 2, 3, 4, 5));
        File.SetAttributes(source, FileAttributes.ReadOnly | FileAttributes.Hidden);
        var old = Write("dest/a.txt", "old");
        File.SetAttributes(old, FileAttributes.ReadOnly);

        var transfer = new FileTransfer(TransferMode.Copy, P("dest"), new FixedConflictResolver(ConflictAction.Replace))
        {
            UseStreamCopy = streamCopy,
        };
        var result = await transfer.RunAsync([source]);

        Assert.Empty(result.Errors);
        Assert.Equal("new", Read(old));
        Assert.Equal(FileAttributes.ReadOnly | FileAttributes.Hidden, File.GetAttributes(old) & (FileAttributes.ReadOnly | FileAttributes.Hidden));
        Assert.Equal(new DateTime(2023, 1, 2, 3, 4, 5), File.GetLastWriteTime(old));
    }

    [Fact]
    public async Task Copies_paths_longer_than_max_path()
    {
        var deep = string.Join('/', Enumerable.Repeat(new string('d', 60), 5));
        var source = Write(deep + "/file.txt", "deep");
        Assert.True(source.Length > 300);

        var result = await Run(TransferMode.Copy, Dir("dest"), ConflictAction.Skip, P(new string('d', 60)));

        Assert.Empty(result.Errors);
        Assert.Equal("deep", Read(P(["dest", .. deep.Split('/'), "file.txt"])));
    }

    [Fact]
    public async Task Pause_holds_the_copy_until_resumed()
    {
        var big = P("big.bin");
        await File.WriteAllBytesAsync(big, new byte[8 << 20]);
        Dir("dest");
        var pause = new PauseTokenSource();
        var paused = new TaskCompletionSource();
        var progress = new CallbackProgress(p =>
        {
            if (p.BytesDone > 0 && !pause.IsPaused && !paused.Task.IsCompleted)
            {
                pause.Pause();
                paused.TrySetResult();
            }
        });

        var transfer = new FileTransfer(TransferMode.Copy, P("dest"), new FixedConflictResolver(ConflictAction.Skip),
            progress, pause.Token) { ReportIntervalMs = 0 };
        var running = transfer.RunAsync([big]);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(200);
        Assert.False(running.IsCompleted);

        pause.Resume();
        var result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(result.Errors);
        Assert.Equal(8 << 20, new FileInfo(P("dest", "big.bin")).Length);
    }

    [Fact]
    public async Task Reports_progress_up_to_the_total()
    {
        Write("src/a.txt", "12345");
        Write("src/b.txt", "678");
        TransferProgress? last = null;

        await FileTransfer.RunAsync(TransferMode.Copy, [P("src")], Dir("dest"),
            new FixedConflictResolver(ConflictAction.Skip), new CallbackProgress(p => last = p));

        Assert.NotNull(last);
        Assert.Equal(8, last.BytesTotal);
        Assert.Equal(8, last.BytesDone);
        Assert.Equal(2, last.ItemsDone);
    }

    private sealed class CountingResolver(ConflictDecision decision) : IConflictResolver
    {
        public int Calls { get; private set; }

        public Task<ConflictDecision> ResolveAsync(FileConflict conflict, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(decision);
        }
    }
}

/// <summary>Synchronous IProgress: Progress&lt;T&gt; would post to the thread pool and race the asserts.</summary>
internal sealed class CallbackProgress(Action<TransferProgress> callback) : IProgress<TransferProgress>
{
    public void Report(TransferProgress value) => callback(value);
}

public class UndoTests : TempFolderTest
{
    [Fact]
    public async Task Undo_copy_removes_the_copies_only()
    {
        Write("src/a.txt");
        Write("src/sub/b.txt");
        Write("dest/keep.txt");

        var result = await Run(TransferMode.Copy, P("dest"), ConflictAction.Skip, P("src"));
        var errors = await result.Journal.UndoAsync();

        Assert.Empty(errors);
        Assert.False(Directory.Exists(P("dest", "src")));
        Assert.True(File.Exists(P("dest", "keep.txt")));
        Assert.True(File.Exists(P("src", "sub", "b.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Undo_move_puts_everything_back(bool crossVolume)
    {
        Write("src/photos/1.jpg", "one");
        Write("dest/photos/2.jpg", "two");
        Write("src/a.txt", "a");

        var transfer = new FileTransfer(TransferMode.Move, P("dest"), new FixedConflictResolver(ConflictAction.Skip))
        {
            ForceCrossVolume = crossVolume,
        };
        var result = await transfer.RunAsync([P("src", "photos"), P("src", "a.txt")]);
        var errors = await result.Journal.UndoAsync();

        Assert.Empty(errors);
        Assert.Equal("one", Read(P("src", "photos", "1.jpg")));
        Assert.Equal("a", Read(P("src", "a.txt")));
        Assert.Equal("two", Read(P("dest", "photos", "2.jpg")));
        Assert.False(File.Exists(P("dest", "photos", "1.jpg")));
        Assert.False(File.Exists(P("dest", "a.txt")));
    }

    [Fact]
    public async Task Undo_rename_and_new_folder()
    {
        var file = Write("notes.txt");
        var (renamed, renameJournal) = FileOps.Rename(file, "todo.txt");
        var (folder, folderJournal) = FileOps.CreateFolder(Root, "New folder");

        Assert.Equal(P("todo.txt"), renamed);
        Assert.Equal(P("New folder"), folder);

        await folderJournal.UndoAsync();
        await renameJournal.UndoAsync();

        Assert.False(Directory.Exists(folder));
        Assert.True(File.Exists(file));
        Assert.False(File.Exists(renamed));
    }
}

public class FileOpsTests : TempFolderTest
{
    [Fact]
    public void Rename_changes_only_the_case()
    {
        var file = Write("readme.md");
        var dir = Dir("docs");

        FileOps.Rename(file, "README.md");
        FileOps.Rename(dir, "Docs");

        Assert.Contains("README.md", Directory.GetFiles(Root).Select(Path.GetFileName));
        Assert.Contains("Docs", Directory.GetDirectories(Root).Select(Path.GetFileName));
    }

    [Fact]
    public void Rename_rejects_bad_names_and_existing_ones()
    {
        var file = Write("a.txt");
        Write("b.txt");

        Assert.Throws<ArgumentException>(() => FileOps.Rename(file, "a:b"));
        Assert.Throws<ArgumentException>(() => FileOps.Rename(file, "   "));
        Assert.Throws<IOException>(() => FileOps.Rename(file, "b.txt"));
    }

    [Fact]
    public void New_folders_get_numbered()
    {
        var (first, _) = FileOps.CreateFolder(Root, "New folder");
        var (second, _) = FileOps.CreateFolder(Root, "New folder");

        Assert.Equal(P("New folder"), first);
        Assert.Equal(P("New folder (2)"), second);
    }

    [Fact]
    public async Task Permanent_delete_removes_read_only_files_and_trees()
    {
        var ro = Write("ro.txt");
        File.SetAttributes(ro, FileAttributes.ReadOnly);
        Write("tree/sub/x.txt");

        var errors = await FileOps.DeletePermanentlyAsync([ro, P("tree")]);

        Assert.Empty(errors);
        Assert.False(File.Exists(ro));
        Assert.False(Directory.Exists(P("tree")));
    }

    [Fact]
    public void Unique_names_count_up()
    {
        Assert.Equal("report.pdf", FileNames.Unique(Root, "report.pdf", isDirectory: false));
        Write("report.pdf");
        Assert.Equal("report (2).pdf", FileNames.Unique(Root, "report.pdf", isDirectory: false));
        Write("report (2).pdf");
        Assert.Equal("report (3).pdf", FileNames.Unique(Root, "report.pdf", isDirectory: false));
        Assert.Equal("report (3).pdf", FileNames.Unique(Root, "report (2).pdf", isDirectory: false));
    }

    [Fact]
    public void Unique_folder_names_ignore_dots()
    {
        Dir("my.folder");
        Assert.Equal("my.folder (2)", FileNames.Unique(Root, "my.folder", isDirectory: true));
    }

    [Theory]
    [InlineData("ok.txt", NameProblem.None)]
    [InlineData("  ", NameProblem.Empty)]
    [InlineData("..", NameProblem.Empty)]
    [InlineData("a?b", NameProblem.InvalidCharacters)]
    [InlineData("CON.txt", NameProblem.InvalidCharacters)]
    public void Validates_names(string name, NameProblem expected) => Assert.Equal(expected, FileNames.Validate(name));
}
