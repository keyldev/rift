using System.Buffers.Binary;

namespace Rift.Core.Tests;

public class FileSorterTests
{
    private static FileEntry F(string name, long size, int day, bool dir = false) =>
        new(name, $@"C:\x\{name}", dir, dir ? null : size, new DateTime(2026, 1, day), dir ? FileKind.Folder : FileKinds.FromExtension(Path.GetExtension(name)), false);

    private static readonly FileEntry[] Entries =
    [
        F("b.txt", 300, 2), F("a.png", 100, 3), F("zeta", 0, 1, dir: true), F("c10.txt", 200, 1), F("c2.txt", 200, 5),
    ];

    [Fact]
    public void Folders_stay_first_whatever_the_column()
    {
        Assert.Equal("zeta", FileSorter.Sort(Entries, SortColumn.Size, descending: true)[0].Name);
        Assert.Equal("zeta", FileSorter.Sort(Entries, SortColumn.Name, descending: true)[0].Name);
    }

    [Fact]
    public void Sorts_by_each_column()
    {
        Assert.Equal(["zeta", "a.png", "b.txt", "c2.txt", "c10.txt"], FileSorter.Sort(Entries, SortColumn.Name, false).Select(e => e.Name));
        Assert.Equal(["zeta", "b.txt", "c2.txt", "c10.txt", "a.png"], FileSorter.Sort(Entries, SortColumn.Size, true).Select(e => e.Name));
        Assert.Equal(["zeta", "c2.txt", "a.png", "b.txt", "c10.txt"], FileSorter.Sort(Entries, SortColumn.Modified, true).Select(e => e.Name));
        // Type sorts by the label shown: Document before Image.
        Assert.Equal(["zeta", "b.txt", "c2.txt", "c10.txt", "a.png"], FileSorter.Sort(Entries, SortColumn.Type, false).Select(e => e.Name));
    }
}

public class NameFilterTests
{
    [Theory]
    [InlineData("rep", "Report.pdf", true)]
    [InlineData("rep", "notes.txt", false)]
    [InlineData("*.pdf", "Report.PDF", true)]
    [InlineData("*.pdf", "pdf.txt", false)]
    [InlineData("IMG_20??*", "IMG_2024_01.jpg", true)]
    [InlineData("a.b", "aXb", false)] // a dot is a dot, not "any character"
    [InlineData("", "anything", true)]
    public void Matches(string filter, string name, bool expected) => Assert.Equal(expected, new NameFilter(filter).Matches(name));
}

public class FuzzyMatcherTests
{
    [Fact]
    public void Needs_the_letters_in_order()
    {
        Assert.NotNull(FuzzyMatcher.Score("nf", "New folder"));
        Assert.Null(FuzzyMatcher.Score("fn", "New folder"));
    }

    [Fact]
    public void Prefers_prefixes_and_word_starts()
    {
        var items = new[] { "Search in folder", "New folder", "Show hidden files" };
        Assert.Equal("New folder", items.OrderByDescending(i => FuzzyMatcher.Score("nf", i)).First());
        Assert.Equal("Show hidden files", items.OrderByDescending(i => FuzzyMatcher.Score("hid", i)).First());
        Assert.True(FuzzyMatcher.Score("down", "Downloads") > FuzzyMatcher.Score("down", "Move down"));
    }
}

public class AddressResolverTests : TempFolderTest
{
    [Fact]
    public void Resolves_folders_files_and_relative_paths()
    {
        var docs = Dir("docs");
        var file = Write("docs/a.txt");

        Assert.Equal((AddressKind.Folder, docs), AddressResolver.Resolve("docs", Root));
        Assert.Equal((AddressKind.Folder, docs), AddressResolver.Resolve($"\"{docs}\\\"", Root));
        Assert.Equal((AddressKind.File, file), AddressResolver.Resolve(@"docs\a.txt", Root));
        Assert.Equal((AddressKind.Folder, Root), AddressResolver.Resolve("..", docs));
        Assert.Equal(AddressKind.NotFound, AddressResolver.Resolve("nope", Root).Kind);
        Assert.Equal((AddressKind.Folder, Locations.RecycleBin), AddressResolver.Resolve("shell:RecycleBinFolder", Root));
    }

    [Fact]
    public void Expands_environment_variables()
    {
        Environment.SetEnvironmentVariable("RIFT_TEST_ROOT", Root);
        Dir("x");
        Assert.Equal((AddressKind.Folder, P("x")), AddressResolver.Resolve(@"%RIFT_TEST_ROOT%\x", @"C:\"));
    }

    [Fact]
    public void Suggests_subfolders_by_prefix()
    {
        Dir("Projects");
        Dir("Photos");
        Dir("Music");
        Write("Pnotafolder.txt");

        Assert.Equal([Root + @"\Photos", Root + @"\Projects"], AddressResolver.Suggest(Root + @"\P", @"C:\"));
        Assert.Equal(3, AddressResolver.Suggest(Root + @"\", @"C:\").Count);
        Assert.Empty(AddressResolver.Suggest("no backslash", Root));
    }
}

public class FileSearchTests : TempFolderTest
{
    [Fact]
    public void Finds_matches_in_subfolders_in_batches()
    {
        Write("a/report-2025.pdf");
        Write("a/b/report-2026.pdf");
        Write("a/b/notes.txt");
        Dir("reports");

        var found = new List<FileEntry>();
        var capped = FileSearch.Run(Root, "report", includeHidden: false, found.AddRange, CancellationToken.None);

        Assert.False(capped);
        Assert.Equal(["report-2025.pdf", "report-2026.pdf", "reports"], found.Select(e => e.Name).Order());
        Assert.Equal(@"a\b", found.Single(e => e.Name == "report-2026.pdf").TypeNote);
        Assert.Equal(".", found.Single(e => e.Name == "reports").TypeNote);
    }

    [Fact]
    public void Stops_when_canceled()
    {
        for (var i = 0; i < 50; i++)
            Write($"f{i}.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => FileSearch.Run(Root, "f", false, _ => { }, cts.Token));
    }
}

public class ImageInfoTests
{
    [Fact]
    public void Reads_png_gif_bmp_and_jpeg_sizes()
    {
        var png = new byte[32];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 640);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), 480);
        Assert.Equal((640, 480), ImageInfo.TryGetSize(new MemoryStream(png)));

        var gif = new byte[32];
        "GIF89a"u8.CopyTo(gif);
        BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(6), 320);
        BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(8), 200);
        Assert.Equal((320, 200), ImageInfo.TryGetSize(new MemoryStream(gif)));

        var bmp = new byte[32];
        "BM"u8.CopyTo(bmp);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 100);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), -50);
        Assert.Equal((100, 50), ImageInfo.TryGetSize(new MemoryStream(bmp)));

        // SOI, an APP0 segment to skip, then SOF0 with height 300 and width 400.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x01, 0x2C, 0x01, 0x90,
            .. new byte[20]];
        Assert.Equal((400, 300), ImageInfo.TryGetSize(new MemoryStream(jpeg)));

        Assert.Null(ImageInfo.TryGetSize(new MemoryStream(new byte[40])));
    }
}

public class LibrariesTests
{
    [Fact]
    public void Parses_names_and_folders()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <libraryDescription xmlns="http://schemas.microsoft.com/windows/2009/library">
              <name>@shell32.dll,-34575</name>
              <searchConnectorDescriptionList>
                <searchConnectorDescription><simpleLocation><url>knownfolder:{FDD39AD0-238F-46AF-ADB4-6C85480369C7}</url></simpleLocation></searchConnectorDescription>
                <searchConnectorDescription><simpleLocation><url>D:\Work\Docs</url></simpleLocation></searchConnectorDescription>
              </searchConnectorDescriptionList>
            </libraryDescription>
            """;
        var library = Libraries.Parse(@"C:\libs\Documents.library-ms", xml, _ => @"C:\Users\me\Documents", s => s == "@shell32.dll,-34575" ? "Documents" : null);

        Assert.NotNull(library);
        Assert.Equal("Documents", library.Name);
        Assert.Equal([@"C:\Users\me\Documents", @"D:\Work\Docs"], library.Folders);
        Assert.Null(Libraries.Parse("x.library-ms", "not xml", _ => null, _ => null));
    }
}

public class PlacesTests : TempFolderTest
{
    [Fact]
    public void Knows_network_and_library_places()
    {
        Assert.Equal(LocationKind.Server, Locations.KindOf(@"\\nas"));
        Assert.Equal(LocationKind.Folder, Locations.KindOf(@"\\nas\media"));
        Assert.Equal(@"\\nas", Locations.Parent(@"\\nas\media"));
        Assert.Equal(Locations.Network, Locations.Parent(@"\\nas"));

        var lib = Locations.Library("Documents", @"C:\x\Documents.library-ms");
        Assert.Equal(LocationKind.Library, Locations.KindOf(lib));
        Assert.Equal("Documents", Locations.Title(lib));
        Assert.Equal(Locations.LibrariesFolder, Locations.Parent(lib));

        var search = Locations.Search(@"C:\Projects", "*.cs");
        Assert.Equal(LocationKind.Search, Locations.KindOf(search));
        Assert.Equal(@"C:\Projects", Locations.Parent(search));
    }

    [Fact]
    public void Pinned_folders_survive_a_restart()
    {
        var file = P("pinned.txt");
        var pins = new PinnedFolders(file);
        Assert.True(pins.Pin(@"C:\a"));
        Assert.False(pins.Pin(@"c:\A"));
        pins.Pin(@"C:\b");
        pins.Unpin(@"C:\a");
        Assert.Equal([@"C:\b"], new PinnedFolders(file).Items);
    }
}
