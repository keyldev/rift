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
