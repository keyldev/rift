using Avalonia;
using Avalonia.Controls;

namespace Rift.App.Controls;

/// <summary>
/// Lays breadcrumbs out in a row; when they don't fit, the row slides left so the current folder
/// (the end of the path) stays visible and the drive end is cut off instead.
/// </summary>
public sealed class CrumbPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(double.IsFinite(availableSize.Width) ? Math.Min(width, availableSize.Width) : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var total = Children.Sum(c => c.DesiredSize.Width);
        var x = Math.Min(0, finalSize.Width - total);
        foreach (var child in Children)
        {
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width;
        }
        return finalSize;
    }
}
