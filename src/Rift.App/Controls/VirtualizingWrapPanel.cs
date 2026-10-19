using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Rift.App.Controls;

/// <summary>
/// A grid of same-size cells that only creates controls for the rows on screen, so the tiles and
/// thumbnail views stay fast in a folder of 100,000 files. Avalonia ships no virtualizing wrap panel.
/// Leftover width is spread between the columns, so the grid always fills the row.
/// </summary>
public sealed class VirtualizingWrapPanel : VirtualizingPanel
{
    public static readonly StyledProperty<double> ItemWidthProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(ItemWidth), 120);

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(ItemHeight), 120);

    /// <summary>Rows realized above and below the viewport, so short scrolls don't show empty cells.</summary>
    private const int BufferRows = 2;

    private static readonly AttachedProperty<object?> RecycleKeyProperty =
        AvaloniaProperty.RegisterAttached<VirtualizingWrapPanel, Control, object?>("RecycleKey");

    private readonly Dictionary<int, Control> _realized = [];
    private readonly Dictionary<object, Stack<Control>> _recyclePool = [];
    private Rect _viewport;
    private int _columns = 1;
    private double _cellWidth;

    static VirtualizingWrapPanel()
    {
        AffectsMeasure<VirtualizingWrapPanel>(ItemWidthProperty, ItemHeightProperty);
    }

    public VirtualizingWrapPanel()
    {
        EffectiveViewportChanged += (_, e) =>
        {
            if (e.EffectiveViewport != _viewport)
            {
                _viewport = e.EffectiveViewport;
                InvalidateMeasure();
            }
        };
    }

    public double ItemWidth { get => GetValue(ItemWidthProperty); set => SetValue(ItemWidthProperty, value); }
    public double ItemHeight { get => GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Items;
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : _viewport.Width;
        _columns = Math.Max(1, (int)Math.Floor(width / ItemWidth));
        _cellWidth = Math.Max(ItemWidth, width / _columns);
        var rows = (items.Count + _columns - 1) / _columns;

        // Which rows are on screen; before the first layout pass there's no viewport yet, so assume one screen.
        var top = Math.Max(0, _viewport.Top);
        var height = _viewport.Height > 0 ? _viewport.Height : 800;
        var firstRow = Math.Max(0, (int)Math.Floor(top / ItemHeight) - BufferRows);
        var lastRow = Math.Min(rows - 1, (int)Math.Ceiling((top + height) / ItemHeight) + BufferRows);
        var first = firstRow * _columns;
        var last = Math.Min(items.Count - 1, (lastRow + 1) * _columns - 1);

        Realize(first, last);

        var cell = new Size(_cellWidth, ItemHeight);
        foreach (var container in _realized.Values)
            container.Measure(cell);
        return new Size(width, rows * ItemHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (index, container) in _realized)
            container.Arrange(CellRect(index));
        return finalSize;
    }

    private Rect CellRect(int index) =>
        new(index % _columns * _cellWidth, index / _columns * ItemHeight, _cellWidth, ItemHeight);

    /// <summary>
    /// Makes exactly [first, last] realized. Containers keep following their item when indexes shift
    /// (a file appeared above), so the focused tile isn't torn down by a folder refresh.
    /// </summary>
    private void Realize(int first, int last)
    {
        var items = Items;
        var byItem = new Dictionary<object, Control>(ReferenceEqualityComparer.Instance);
        foreach (var container in _realized.Values)
        {
            if (container.DataContext is { } item)
                byItem.TryAdd(item, container);
        }

        var next = new Dictionary<int, Control>();
        for (var i = first; i <= last && i >= 0; i++)
        {
            var item = items[i];
            if (item is not null && byItem.Remove(item, out var existing))
            {
                var oldIndex = _realized.First(p => p.Value == existing).Key;
                if (oldIndex != i)
                    ItemContainerGenerator!.ItemContainerIndexChanged(existing, oldIndex, i);
                next[i] = existing;
            }
            else
            {
                next[i] = GetContainer(item, i);
            }
        }

        // What's left went off screen or out of the list. Keep the focused one alive, or keyboard focus would be lost.
        foreach (var container in byItem.Values)
        {
            var index = container.DataContext is { } item ? IndexOf(item) : -1;
            if (container.IsKeyboardFocusWithin && index >= 0 && !next.ContainsKey(index))
                next[index] = container;
            else
                Recycle(container);
        }
        foreach (var container in _realized.Values.Where(c => c.DataContext is null && !next.ContainsValue(c)))
            Recycle(container);

        _realized.Clear();
        foreach (var (index, container) in next)
            _realized[index] = container;
    }

    private int IndexOf(object item)
    {
        var items = Items;
        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], item))
                return i;
        }
        return -1;
    }

    private Control GetContainer(object? item, int index)
    {
        var generator = ItemContainerGenerator!;
        if (!generator.NeedsContainer(item, index, out var recycleKey))
        {
            // The item is a control itself.
            var control = (Control)item!;
            if (control.Parent is null)
                AddInternalChild(control);
            generator.PrepareItemContainer(control, item, index);
            generator.ItemContainerPrepared(control, item, index);
            return control;
        }

        if (recycleKey is not null && _recyclePool.TryGetValue(recycleKey, out var pool) && pool.TryPop(out var recycled))
        {
            recycled.SetCurrentValue(IsVisibleProperty, true);
            generator.PrepareItemContainer(recycled, item, index);
            generator.ItemContainerPrepared(recycled, item, index);
            return recycled;
        }

        var container = generator.CreateContainer(item, index, recycleKey);
        container.SetValue(RecycleKeyProperty, recycleKey);
        AddInternalChild(container);
        generator.PrepareItemContainer(container, item, index);
        generator.ItemContainerPrepared(container, item, index);
        return container;
    }

    private void Recycle(Control container)
    {
        var key = container.GetValue(RecycleKeyProperty);
        if (key is null)
        {
            RemoveInternalChild(container);
            return;
        }
        ItemContainerGenerator!.ClearItemContainer(container);
        container.SetCurrentValue(IsVisibleProperty, false);
        if (!_recyclePool.TryGetValue(key, out var pool))
            _recyclePool[key] = pool = new Stack<Control>();
        pool.Push(container);
    }

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(items, e);
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var container in _realized.Values)
                Recycle(container);
            _realized.Clear();
        }
        InvalidateMeasure();
    }

    protected override Control? ContainerFromIndex(int index) => _realized.GetValueOrDefault(index);

    protected override int IndexFromContainer(Control container)
    {
        foreach (var (index, c) in _realized)
        {
            if (c == container)
                return index;
        }
        return -1;
    }

    protected override IEnumerable<Control>? GetRealizedContainers() => _realized.Values;

    protected override Control? ScrollIntoView(int index)
    {
        if (index < 0 || index >= Items.Count)
            return null;
        if (!_realized.ContainsKey(index))
        {
            // Realize it right away, so focus can move there before the next layout pass.
            var container = GetContainer(Items[index], index);
            _realized[index] = container;
            container.Measure(new Size(_cellWidth, ItemHeight));
            container.Arrange(CellRect(index));
        }
        var target = _realized[index];
        target.BringIntoView();
        return target;
    }

    /// <summary>
    /// The row a page above or below <paramref name="current"/>. ListBox scrolls on PageUp/PageDown by itself
    /// instead of asking the panel, so the pane calls this directly.
    /// </summary>
    public int PageTarget(int current, bool down)
    {
        var count = Items.Count;
        if (count == 0)
            return -1;
        var step = Math.Max(1, (int)(_viewport.Height / ItemHeight) - 1) * _columns;
        return Math.Clamp(current < 0 ? 0 : down ? current + step : current - step, 0, count - 1);
    }

    /// <summary>Arrow keys move through the grid: left/right by one, up/down by a row; Home/End, PageUp/PageDown.</summary>
    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        var count = Items.Count;
        if (count == 0)
            return null;
        var current = from is Control c ? IndexFromContainer(c) : -1;
        var pageRows = Math.Max(1, (int)(_viewport.Height / ItemHeight) - 1);
        var target = direction switch
        {
            NavigationDirection.First => 0,
            NavigationDirection.Last => count - 1,
            _ when current < 0 => 0,
            NavigationDirection.Left or NavigationDirection.Previous => current - 1,
            NavigationDirection.Right or NavigationDirection.Next => current + 1,
            NavigationDirection.Up => current - _columns,
            NavigationDirection.Down => Math.Min(count - 1, current + _columns),
            NavigationDirection.PageUp => Math.Max(0, current - pageRows * _columns),
            NavigationDirection.PageDown => Math.Min(count - 1, current + pageRows * _columns),
            _ => current,
        };
        if (target < 0 || target >= count)
            return null;
        return ScrollIntoView(target);
    }
}
