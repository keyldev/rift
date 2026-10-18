using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Rift.App.ViewModels;
using Rift.Core;

namespace Rift.App.Controls;

/// <summary>
/// Turns the layout tree into nested grids with splitters. Pane views are created once and moved
/// between rebuilds, so a split or a closed neighbour doesn't reset scroll position or selection.
/// </summary>
public sealed class LayoutHost : Decorator
{
    private const double Gap = 8;
    private const double MinPane = 220;

    private readonly Dictionary<PaneViewModel, Control> _views = [];

    /// <summary>Makes the view for a pane the first time it's needed.</summary>
    public Func<PaneViewModel, Control>? CreateView { get; set; }

    /// <summary>A splitter was dragged: the new share of the first part.</summary>
    public Action<LayoutSplit<PaneViewModel>, double>? RatioChanged { get; set; }

    public Control? ViewFor(PaneViewModel pane) => _views.GetValueOrDefault(pane);

    /// <param name="only">Show just this pane (zoomed), or null for the whole layout.</param>
    public void Build(LayoutNode<PaneViewModel> root, PaneViewModel? only)
    {
        Child = null;
        var alive = root.Leaves.ToHashSet();
        foreach (var gone in _views.Keys.Where(p => !alive.Contains(p)).ToList())
        {
            Detach(_views[gone]);
            _views.Remove(gone);
        }
        foreach (var view in _views.Values)
            Detach(view);

        Child = only is not null ? View(only) : Node(root);
    }

    private Control Node(LayoutNode<PaneViewModel> node) => node switch
    {
        LayoutLeaf<PaneViewModel> leaf => View(leaf.Value),
        LayoutSplit<PaneViewModel> split => Split(split),
        _ => throw new InvalidOperationException(),
    };

    private Control View(PaneViewModel pane)
    {
        if (!_views.TryGetValue(pane, out var view))
        {
            view = CreateView!(pane);
            _views[pane] = view;
        }
        return view;
    }

    private Grid Split(LayoutSplit<PaneViewModel> split)
    {
        var horizontal = split.Direction == SplitDirection.Horizontal;
        var grid = new Grid();
        var first = Node(split.First);
        var second = Node(split.Second);
        var splitter = new GridSplitter
        {
            ResizeDirection = horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows,
            Background = Avalonia.Media.Brushes.Transparent,
            Cursor = new Cursor(horizontal ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth),
        };

        if (horizontal)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(split.Ratio, GridUnitType.Star) { MinWidth = MinPane });
            grid.ColumnDefinitions.Add(new ColumnDefinition(Gap, GridUnitType.Pixel));
            grid.ColumnDefinitions.Add(new ColumnDefinition(1 - split.Ratio, GridUnitType.Star) { MinWidth = MinPane });
            Place(first, 0, 0);
            Place(splitter, 0, 1);
            Place(second, 0, 2);
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition(split.Ratio, GridUnitType.Star) { MinHeight = MinPane * 0.75 });
            grid.RowDefinitions.Add(new RowDefinition(Gap, GridUnitType.Pixel));
            grid.RowDefinitions.Add(new RowDefinition(1 - split.Ratio, GridUnitType.Star) { MinHeight = MinPane * 0.75 });
            Place(first, 0, 0);
            Place(splitter, 1, 0);
            Place(second, 2, 0);
        }

        splitter.DragCompleted += (_, _) =>
        {
            double a = horizontal ? first.Bounds.Width : first.Bounds.Height;
            double b = horizontal ? second.Bounds.Width : second.Bounds.Height;
            if (a + b > 0)
                RatioChanged?.Invoke(split, a / (a + b));
        };

        grid.Children.Add(first);
        grid.Children.Add(splitter);
        grid.Children.Add(second);
        return grid;
    }

    /// <summary>Sets both coordinates: a reused pane view still carries the ones from its previous spot.</summary>
    private static void Place(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
    }

    private static void Detach(Control view)
    {
        switch (view.Parent)
        {
            case Panel panel:
                panel.Children.Remove(view);
                break;
            case Decorator decorator:
                decorator.Child = null;
                break;
        }
    }
}
