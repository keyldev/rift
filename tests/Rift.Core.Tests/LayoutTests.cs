namespace Rift.Core.Tests;

public class LayoutTreeTests
{
    private sealed record P(string Name);

    [Fact]
    public void Splits_and_lists_panes_in_reading_order()
    {
        P a = new("a"), b = new("b"), c = new("c"), d = new("d");
        var tree = new LayoutTree<P>(a);
        tree.Split(a, b, SplitDirection.Horizontal);   // a | b
        tree.Split(b, c, SplitDirection.Vertical);     // a | (b / c)
        tree.Split(a, d, SplitDirection.Horizontal, before: true); // (d | a) | (b / c)

        Assert.Equal([d, a, b, c], tree.Leaves);
        Assert.IsType<LayoutSplit<P>>(tree.Root);
        Assert.Same(c, tree.Next(b));
        Assert.Same(d, tree.Next(c));
        Assert.Same(c, tree.Next(d, -1));
    }

    [Fact]
    public void Removing_a_pane_gives_its_space_to_the_sibling()
    {
        P a = new("a"), b = new("b"), c = new("c");
        var tree = new LayoutTree<P>(a);
        tree.Split(a, b, SplitDirection.Horizontal);
        tree.Split(b, c, SplitDirection.Vertical);

        Assert.True(tree.Remove(b));
        Assert.Equal([a, c], tree.Leaves);
        var root = Assert.IsType<LayoutSplit<P>>(tree.Root);
        Assert.Same(c, Assert.IsType<LayoutLeaf<P>>(root.Second).Value);
        Assert.Same(root, root.Second.Parent);

        Assert.True(tree.Remove(a));
        Assert.Same(c, Assert.IsType<LayoutLeaf<P>>(tree.Root).Value);
        Assert.Null(tree.Root.Parent);
        Assert.False(tree.Remove(c)); // the last pane stays
    }

    [Fact]
    public void Ratio_stays_in_bounds()
    {
        var split = new LayoutSplit<P>(SplitDirection.Horizontal, new LayoutLeaf<P>(new("a")), new LayoutLeaf<P>(new("b")), 2);
        Assert.Equal(1 - LayoutSplit<P>.MinRatio, split.Ratio);
        split.Ratio = double.NaN;
        Assert.Equal(0.5, split.Ratio);
    }
}

public class SessionStoreTests : TempFolderTest
{
    private static LayoutState Pane(params string[] paths) =>
        new() { Pane = new PaneState { Tabs = paths.Select(p => new TabState { Path = p }).ToList() } };

    [Fact]
    public void Round_trips_layout_tabs_and_window()
    {
        var state = new SessionState
        {
            Layout = new LayoutState
            {
                Direction = SplitDirection.Horizontal,
                Ratio = 0.3,
                First = Pane(@"C:\a", @"C:\b"),
                Second = new LayoutState { Direction = SplitDirection.Vertical, First = Pane(@"D:\c"), Second = Pane("shell:RecycleBinFolder") },
            },
            ActivePane = 2,
            Window = new WindowPlacement { X = 10, Y = 20, Width = 1000, Height = 700, Maximized = true },
        };
        state.Layout.First!.Pane!.Tabs[1] = state.Layout.First.Pane.Tabs[1] with { Pinned = true, View = "Thumbnails", Sort = SortColumn.Size, Descending = true };

        var file = P("session.json");
        SessionStore.Save(file, state);
        var loaded = SessionStore.Load(file);

        Assert.NotNull(loaded);
        Assert.Equal(0.3, loaded.Layout.Ratio);
        Assert.Equal(SplitDirection.Vertical, loaded.Layout.Second!.Direction);
        Assert.Equal(state.Layout.First.Pane.Tabs[1], loaded.Layout.First!.Pane!.Tabs[1]);
        Assert.Equal("shell:RecycleBinFolder", loaded.Layout.Second.Second!.Pane!.Tabs[0].Path);
        Assert.Equal(2, loaded.ActivePane);
        Assert.True(loaded.Window!.Maximized);
        Assert.Contains("\"thumbnails\"", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Broken_parts_are_dropped_and_garbage_is_ignored()
    {
        var file = P("session.json");
        // The right side has a pane with no tabs: the split collapses into the left pane.
        SessionStore.Save(file, new SessionState
        {
            Layout = new LayoutState { First = Pane(@"C:\a"), Second = new LayoutState { Pane = new PaneState() } },
        });
        var loaded = SessionStore.Load(file);
        Assert.Equal(@"C:\a", loaded!.Layout.Pane!.Tabs[0].Path);

        File.WriteAllText(file, "{ not json");
        Assert.Null(SessionStore.Load(file));
        Assert.Null(SessionStore.Load(P("missing.json")));

        SessionStore.Save(file, new SessionState { Layout = new LayoutState { Pane = new PaneState() } });
        Assert.Null(SessionStore.Load(file));
    }

    [Fact]
    public void Workspace_names_become_safe_file_names()
    {
        var file = SessionStore.WorkspaceFile("work: \"photos\"?");
        Assert.DoesNotContain('?', Path.GetFileName(file));
        Assert.EndsWith(".json", file);
    }
}

public class SettingsStoreTests : TempFolderTest
{
    [Fact]
    public void Round_trips_and_falls_back_to_defaults()
    {
        var file = P("settings.json");
        Assert.Equal("azure", SettingsStore.Load(file).Accent); // nothing saved yet

        var state = new SettingsState { Theme = "system", Accent = "mint", Language = "ru", ShowHidden = true };
        state.Hotkeys["file.rename"] = "Shift+F6";
        state.Tags.Add(new TagDefinition("t1", "Work", "#6FD39B"));
        state.Columns.TypeWidth = 140;
        SettingsStore.Save(state, file);

        var loaded = SettingsStore.Load(file);
        Assert.Equal(("system", "mint", "ru", true), (loaded.Theme, loaded.Accent, loaded.Language, loaded.ShowHidden));
        Assert.Equal("Shift+F6", loaded.Hotkeys["file.rename"]);
        Assert.Equal(state.Tags, loaded.Tags);
        Assert.Equal(140, loaded.Columns.TypeWidth);

        File.WriteAllText(file, "][");
        Assert.Equal("dark", SettingsStore.Load(file).Theme);
    }

    [Fact]
    public void Unknown_accent_falls_back_to_the_first()
    {
        Assert.Equal("azure", Accents.Find("plaid").Id);
        Assert.Equal("#FF5C8A", Accents.Find("rose").Dark);
    }
}

public class TagStoreTests : TempFolderTest
{
    [Fact]
    public void Toggles_and_survives_a_restart()
    {
        var file = P("tags.json");
        var tags = new TagStore(file);
        tags.Toggle([@"C:\a.txt", @"C:\b.txt"], "red");
        Assert.True(tags.Has(@"c:\A.TXT", "red"));

        tags.Toggle([@"C:\a.txt"], "red");          // all selected have it: toggling removes
        Assert.False(tags.Has(@"C:\a.txt", "red"));
        tags.Toggle([@"C:\a.txt", @"C:\b.txt"], "red"); // only some have it: toggling adds to all
        Assert.Equal(2, tags.PathsWith("red").Count);

        var reloaded = new TagStore(file);
        Assert.Equal(["red"], reloaded.TagsOf(@"C:\b.txt"));
    }

    [Fact]
    public void Tags_follow_renames_and_folder_moves()
    {
        var tags = new TagStore(null);
        tags.Toggle([@"C:\docs\a.txt", @"C:\docs", @"C:\docsx\b.txt"], "blue");

        tags.Move(@"C:\docs", @"D:\archive\docs");

        Assert.True(tags.Has(@"D:\archive\docs", "blue"));
        Assert.True(tags.Has(@"D:\archive\docs\a.txt", "blue"));
        Assert.True(tags.Has(@"C:\docsx\b.txt", "blue")); // a similar name isn't inside
        Assert.False(tags.Has(@"C:\docs\a.txt", "blue"));

        tags.Forget("blue");
        Assert.Empty(tags.PathsWith("blue"));
    }
}

public class FolderViewStoreTests : TempFolderTest
{
    [Fact]
    public void Remembers_per_folder_and_saves()
    {
        var file = P("views.json");
        var views = new FolderViewStore(file);
        views.Set(@"C:\Photos", new FolderView("Thumbnails", SortColumn.Modified, true));
        views.Save();

        var loaded = new FolderViewStore(file);
        Assert.Equal(new FolderView("Thumbnails", SortColumn.Modified, true), loaded.Get(@"c:\photos"));
        Assert.Null(loaded.Get(@"C:\Other"));
    }
}
