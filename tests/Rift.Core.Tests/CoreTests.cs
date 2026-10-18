using Rift.Core;

namespace Rift.Core.Tests;

public class PathCrumbsTests
{
    [Fact]
    public void Splits_drive_path()
    {
        var crumbs = PathCrumbs.Split(@"C:\Users\keyl\Downloads");

        Assert.Equal(["C:", "Users", "keyl", "Downloads"], crumbs.Select(c => c.Label));
        Assert.Equal(@"C:\", crumbs[0].Path);
        Assert.Equal(@"C:\Users\keyl", crumbs[2].Path);
    }

    [Fact]
    public void Treats_unc_share_as_root()
    {
        var crumbs = PathCrumbs.Split(@"\\nas\media\movies\");

        Assert.Equal([@"\\nas\media", "movies"], crumbs.Select(c => c.Label));
        Assert.Equal(@"\\nas\media\movies", crumbs[1].Path);
    }

    [Theory]
    [InlineData(@"C:\Users\keyl", @"C:\Users")]
    [InlineData(@"C:\Users", @"C:\")]
    [InlineData(@"C:\", null)]
    [InlineData(@"\\nas\media\movies", @"\\nas\media\")]
    public void Parent(string path, string? expected) => Assert.Equal(expected, PathCrumbs.Parent(path));
}

public class NavigationHistoryTests
{
    [Fact]
    public void Back_and_forward_walk_the_history()
    {
        var h = new NavigationHistory(@"C:\");
        h.Push(@"C:\Users");
        h.Push(@"C:\Users\keyl");

        Assert.Equal(@"C:\Users", h.Back());
        Assert.Equal(@"C:\", h.Back());
        Assert.False(h.CanGoBack);
        Assert.Equal(@"C:\Users", h.Forward());
    }

    [Fact]
    public void Push_clears_forward_and_ignores_same_path()
    {
        var h = new NavigationHistory(@"C:\");
        h.Push(@"C:\Users");
        h.Back();
        h.Push(@"D:\");
        h.Push(@"d:\");

        Assert.False(h.CanGoForward);
        Assert.Equal(@"C:\", h.Back());
        Assert.False(h.CanGoBack);
    }
}

public class FormattingTests
{
    [Fact]
    public void Natural_sort_puts_numbers_in_order()
    {
        var names = new[] { "file10.txt", "File2.txt", "file1.txt" }.Order(NaturalStringComparer.Instance);
        Assert.Equal(["file1.txt", "File2.txt", "file10.txt"], names);
    }

    [Theory]
    [InlineData(".PNG", FileKind.Image)]
    [InlineData(".slnx", FileKind.Code)]
    [InlineData(".unknown", FileKind.Other)]
    [InlineData("", FileKind.Other)]
    public void Classifies_extensions(string ext, FileKind kind) => Assert.Equal(kind, FileKinds.FromExtension(ext));

    [Fact]
    public void Formats_sizes()
    {
        Assert.Equal("", SizeFormatter.Format(null));
        Assert.Equal("512 Б", SizeFormatter.Format(512));
        Assert.Equal("400 МБ", SizeFormatter.Format(400L * 1024 * 1024));
    }
}

public class StartupArgsTests
{
    [Fact]
    public void Splits_paths_between_panes()
    {
        var args = StartupArgs.Parse([@"C:\a", "--right", @"D:\b", "--left", @"C:\c", @"C:\d"]);

        Assert.Equal([@"C:\a", @"C:\c", @"C:\d"], args.Left);
        Assert.Equal([@"D:\b"], args.Right);
    }

    [Fact]
    public void Empty_args_give_empty_lists()
    {
        var args = StartupArgs.Parse([]);
        Assert.Empty(args.Left);
        Assert.Empty(args.Right);
    }
}

public class DirectoryReaderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rift-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Lists_folders_first_then_files()
    {
        File.WriteAllText(Path.Combine(_root, "b.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "a.png"), "x");
        Directory.CreateDirectory(Path.Combine(_root, "zeta"));

        var entries = DirectoryReader.Read(_root);

        Assert.Equal(["zeta", "a.png", "b.txt"], entries.Select(e => e.Name));
        Assert.True(entries[0].IsDirectory);
        Assert.Equal(FileKind.Image, entries[1].Kind);
        Assert.Equal(1, entries[2].Size);
    }
}
