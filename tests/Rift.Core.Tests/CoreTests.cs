using System.Collections;
using System.Globalization;
using System.Resources;
using Rift.Core;
using Rift.Core.Localization;

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

[Collection("Culture")]
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

    [Theory]
    [InlineData("en", "512 B", "400 MB")]
    [InlineData("ru", "512 Б", "400 МБ")]
    public void Formats_sizes(string culture, string bytes, string megabytes)
    {
        using var _ = new UseCulture(culture);
        Assert.Equal("", SizeFormatter.Format(null));
        Assert.Equal(bytes, SizeFormatter.Format(512));
        Assert.Equal(megabytes, SizeFormatter.Format(400L * 1024 * 1024));
    }
}

/// <summary>Switches Rift's UI language for one test.</summary>
internal sealed class UseCulture : IDisposable
{
    private readonly CultureInfo? _previous = Strings.Culture;

    public UseCulture(string name) => Strings.Culture = new CultureInfo(name);

    public void Dispose() => Strings.Culture = _previous;
}

[Collection("Culture")]
public class StringsTests
{
    private static HashSet<string> Keys(string culture)
    {
        var rm = new ResourceManager("Rift.Core.Localization.Strings", typeof(Strings).Assembly);
        var set = rm.GetResourceSet(CultureInfo.GetCultureInfo(culture), createIfNotExists: true, tryParents: false)
                  ?? throw new InvalidOperationException($"No resources for '{culture}'");
        return set.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
    }

    [Fact]
    public void Every_translation_has_the_same_keys_as_english()
    {
        var english = Keys(CultureInfo.InvariantCulture.Name);
        foreach (var culture in Strings.Supported.Where(c => c != "en"))
            Assert.Equal(english.Order(), Keys(culture).Order());
    }

    [Fact]
    public void Every_accessor_has_a_resource()
    {
        var english = Keys(CultureInfo.InvariantCulture.Name);
        var accessors = typeof(Strings).GetMembers()
            .Where(m => m is System.Reflection.PropertyInfo { PropertyType.Name: "String" } or System.Reflection.MethodInfo { ReturnType.Name: "String", IsSpecialName: false })
            .Select(m => m.Name)
            .Where(n => n is not ("Get" or "ToString"))
            .ToHashSet();
        Assert.Empty(accessors.Except(english));
    }

    [Fact]
    public void Russian_is_used_for_ru_culture()
    {
        using var _ = new UseCulture("ru");
        Assert.Equal("Папка", Strings.KindFolder);
        Assert.Equal("5 элем.", Strings.ItemsCount(5));
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
    public void Strips_stray_quote_from_explorer_drive_root()
    {
        // "Rift.exe" "C:\" from the Explorer verb arrives as C:"
        var args = StartupArgs.Parse(["C:\""]);
        Assert.Equal([@"C:\"], args.Left);
    }

    [Fact]
    public void Reads_language_without_treating_it_as_a_path()
    {
        var args = StartupArgs.Parse(["--lang", "EN", @"C:\a"]);
        Assert.Equal("en", args.Language);
        Assert.Equal([@"C:\a"], args.Left);
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
