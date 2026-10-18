using System.Text;
using Rift.Core.Operations;

namespace Rift.Core.Tests;

public class RecycleBinTests : TempFolderTest
{
    private string Bin => Dir("$Recycle.Bin/S-1-5-21-test");

    /// <summary>Puts a file into the fake bin the way Explorer does: $R holds the data, $I the metadata.</summary>
    private RecycledItem Recycle(string original, DateTime deletedAt, string id)
    {
        var data = Path.Combine(Bin, $"$R{id}{Path.GetExtension(original)}");
        var info = Path.Combine(Bin, $"$I{id}{Path.GetExtension(original)}");
        File.Move(original, data);
        File.WriteAllBytes(info, RecycleBinStore.FormatInfo(original, 1, deletedAt));
        return new RecycledItem(data, info, original, 1, deletedAt, false);
    }

    [Fact]
    public void Parses_version_1_and_2_info_files()
    {
        var v2 = RecycleBinStore.FormatInfo(@"C:\Users\me\notes.txt", 1234, new DateTime(2026, 10, 18, 12, 0, 0));
        Assert.Equal((@"C:\Users\me\notes.txt", 1234L, new DateTime(2026, 10, 18, 12, 0, 0)), RecycleBinStore.ParseInfo(v2));

        var v1 = new byte[24 + 520];
        BitConverter.GetBytes(1L).CopyTo(v1, 0);
        BitConverter.GetBytes(42L).CopyTo(v1, 8);
        BitConverter.GetBytes(new DateTime(2015, 1, 2).ToFileTime()).CopyTo(v1, 16);
        Encoding.Unicode.GetBytes(@"D:\old.doc").CopyTo(v1, 24);
        Assert.Equal((@"D:\old.doc", 42L, new DateTime(2015, 1, 2)), RecycleBinStore.ParseInfo(v1));

        Assert.Null(RecycleBinStore.ParseInfo(new byte[10]));
        Assert.Null(RecycleBinStore.ParseInfo(new byte[40]));
    }

    [Fact]
    public void Lists_newest_first_and_skips_orphans()
    {
        Recycle(Write("docs/a.txt"), new DateTime(2026, 1, 1), "AAAAAA");
        Recycle(Write("docs/b.txt"), new DateTime(2026, 2, 1), "BBBBBB");
        File.WriteAllBytes(Path.Combine(Bin, "$ICCCCCC.txt"), RecycleBinStore.FormatInfo(P("gone.txt"), 1, DateTime.Now));

        var items = RecycleBinStore.Read([Bin]);

        Assert.Equal(["b.txt", "a.txt"], items.Select(i => i.Name));
    }

    [Fact]
    public void Restores_to_the_original_place_without_overwriting()
    {
        var original = Write("docs/a.txt", "old");
        var item = Recycle(original, DateTime.Now, "AAAAAA");
        Write("docs/a.txt", "newer");

        var restored = RecycleBinStore.Restore(item);

        Assert.Equal(P("docs", "a (2).txt"), restored);
        Assert.Equal("old", Read(restored));
        Assert.Equal("newer", Read(original));
        Assert.False(File.Exists(item.InfoPath));
        Assert.Empty(RecycleBinStore.Read([Bin]));
    }

    [Fact]
    public void Undo_of_a_delete_brings_files_back()
    {
        var since = DateTime.Now.AddSeconds(-5);
        var a = Write("docs/a.txt");
        var b = Write("docs/b.txt");
        Recycle(a, DateTime.Now.AddDays(-3), "OLD000"); // an earlier delete of the same path
        Write("docs/a.txt");
        Recycle(a, DateTime.Now, "AAAAAA");
        Recycle(b, DateTime.Now, "BBBBBB");

        var found = RecycleBinStore.FindRecent([Bin], [a, b], since);
        Assert.Equal(2, found.Count);

        var journal = new OperationJournal();
        foreach (var item in found)
            journal.AddRecycled(item);
        Assert.Empty(journal.UndoAsync().Result);

        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));
        Assert.Equal(["a.txt"], RecycleBinStore.Read([Bin]).Select(i => i.Name)); // the old one stays
    }

    [Fact]
    public void Deletes_for_good()
    {
        var item = Recycle(Write("x.txt"), DateTime.Now, "AAAAAA");
        RecycleBinStore.Delete(item);
        Assert.False(File.Exists(item.DataPath));
        Assert.False(File.Exists(item.InfoPath));
    }
}

public class LocationsTests
{
    [Fact]
    public void Knows_virtual_places()
    {
        Assert.Equal(LocationKind.ThisPc, Locations.KindOf("shell:MyComputerFolder"));
        Assert.Equal(LocationKind.RecycleBin, Locations.KindOf("SHELL:RecycleBinFolder"));
        Assert.Equal(LocationKind.Folder, Locations.KindOf(@"C:\Users"));
        Assert.Single(Locations.Crumbs(Locations.RecycleBin));
    }

    [Theory]
    [InlineData(@"C:\Users", @"C:\")]
    [InlineData(@"C:\", Locations.ThisPc)]
    [InlineData(Locations.ThisPc, null)]
    [InlineData(@"\\nas\media", @"\\nas")]
    public void Parent_of_a_drive_is_this_pc(string path, string? parent) => Assert.Equal(parent, Locations.Parent(path));
}

public class StartupForwardingTests
{
    [Fact]
    public void Forwards_absolute_paths_per_pane()
    {
        var args = StartupArgs.Parse(["--left", @"C:\a", @"C:\b", "--right", @"D:\c", "--new-window"]);
        Assert.True(args.NewWindow);
        var forwarded = StartupArgs.Parse(args.ToForwardedArgs());
        Assert.Equal(args.Left, forwarded.Left);
        Assert.Equal(args.Right, forwarded.Right);
        Assert.False(forwarded.NewWindow);
    }
}

public class RecentFoldersTests : TempFolderTest
{
    [Fact]
    public void Keeps_newest_first_without_duplicates()
    {
        var file = P("recent.txt");
        var recent = new RecentFolders(file);
        for (var i = 0; i < RecentFolders.Capacity + 3; i++)
            recent.Add($@"C:\f{i}");
        Assert.False(recent.Add($@"c:\F{RecentFolders.Capacity + 2}"));
        recent.Add(@"C:\f5");
        recent.Save();

        var loaded = new RecentFolders(file).Items;
        Assert.Equal(RecentFolders.Capacity, loaded.Count);
        Assert.Equal(@"C:\f5", loaded[0]);
        Assert.Equal(@"C:\f12", loaded[1]);
    }
}
