namespace Rift.Core;

public enum SplitDirection
{
    /// <summary>Side by side, a vertical divider between them.</summary>
    Horizontal,
    /// <summary>One above the other.</summary>
    Vertical,
}

/// <summary>
/// How panes share the window, like editor groups in VS Code: every split divides its space between
/// two parts, and each part is a pane or another split.
/// </summary>
public abstract class LayoutNode<T> where T : class
{
    public LayoutSplit<T>? Parent { get; internal set; }

    /// <summary>Panes from left to right, top to bottom.</summary>
    public abstract IEnumerable<T> Leaves { get; }
}

public sealed class LayoutLeaf<T>(T value) : LayoutNode<T> where T : class
{
    public T Value { get; } = value;
    public override IEnumerable<T> Leaves => [Value];
}

public sealed class LayoutSplit<T> : LayoutNode<T> where T : class
{
    public const double MinRatio = 0.1;

    private LayoutNode<T> _first;
    private LayoutNode<T> _second;
    private double _ratio;

    public LayoutSplit(SplitDirection direction, LayoutNode<T> first, LayoutNode<T> second, double ratio = 0.5)
    {
        Direction = direction;
        _first = first;
        _second = second;
        first.Parent = this;
        second.Parent = this;
        Ratio = ratio;
    }

    public SplitDirection Direction { get; }

    /// <summary>The first part's share of the space, 0.1 to 0.9.</summary>
    public double Ratio
    {
        get => _ratio;
        set => _ratio = Math.Clamp(double.IsFinite(value) ? value : 0.5, MinRatio, 1 - MinRatio);
    }

    public LayoutNode<T> First
    {
        get => _first;
        internal set
        {
            _first = value;
            value.Parent = this;
        }
    }

    public LayoutNode<T> Second
    {
        get => _second;
        internal set
        {
            _second = value;
            value.Parent = this;
        }
    }

    public override IEnumerable<T> Leaves => First.Leaves.Concat(Second.Leaves);
}

/// <summary>Editing operations on a layout; the root changes when the top split appears or goes away.</summary>
public sealed class LayoutTree<T> where T : class
{
    public LayoutTree(T first) => Root = new LayoutLeaf<T>(first);

    public LayoutTree(LayoutNode<T> root)
    {
        Root = root;
        root.Parent = null;
    }

    public LayoutNode<T> Root { get; private set; }

    public IReadOnlyList<T> Leaves => Root.Leaves.ToList();

    public LayoutLeaf<T>? Find(T value) => Find(Root, value);

    private static LayoutLeaf<T>? Find(LayoutNode<T> node, T value) => node switch
    {
        LayoutLeaf<T> leaf when ReferenceEquals(leaf.Value, value) => leaf,
        LayoutSplit<T> split => Find(split.First, value) ?? Find(split.Second, value),
        _ => null,
    };

    /// <summary>Puts <paramref name="added"/> next to <paramref name="target"/>, splitting its space in half.</summary>
    /// <param name="before">Left of / above the target instead of right of / below it.</param>
    public void Split(T target, T added, SplitDirection direction, bool before = false)
    {
        var leaf = Find(target) ?? throw new ArgumentException("Not in the layout.", nameof(target));
        var parent = leaf.Parent;
        var isFirst = parent?.First == leaf;
        var fresh = new LayoutLeaf<T>(added);
        var split = before ? new LayoutSplit<T>(direction, fresh, leaf) : new LayoutSplit<T>(direction, leaf, fresh);
        Replace(parent, isFirst, split);
    }

    /// <summary>Takes a pane out; its sibling takes over the space. The last pane can't be removed.</summary>
    public bool Remove(T value)
    {
        if (Find(value) is not { Parent: { } parent } leaf)
            return false;
        var sibling = parent.First == leaf ? parent.Second : parent.First;
        var grand = parent.Parent;
        Replace(grand, grand?.First == parent, sibling);
        return true;
    }

    /// <summary>The pane after (or before) <paramref name="value"/> in reading order, wrapping around.</summary>
    public T Next(T value, int step = 1)
    {
        var leaves = Leaves;
        var index = leaves.ToList().FindIndex(l => ReferenceEquals(l, value));
        return leaves[((index < 0 ? 0 : index) + step % leaves.Count + leaves.Count) % leaves.Count];
    }

    private void Replace(LayoutSplit<T>? parent, bool isFirst, LayoutNode<T> node)
    {
        if (parent is null)
        {
            Root = node;
            node.Parent = null;
        }
        else if (isFirst)
        {
            parent.First = node;
        }
        else
        {
            parent.Second = node;
        }
    }
}
