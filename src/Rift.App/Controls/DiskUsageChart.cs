using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Rift.Core;

namespace Rift.App.Controls;

public sealed record DiskUsageSlice(DiskUsageCategory Category, double Fraction, IBrush Brush);

/// <summary>
/// Interactive donut chart drawn directly with <see cref="DrawingContext"/>: zero external dependencies,
/// Native AOT friendly, with hit-testing so hovering a slice highlights its legend row and vice versa.
/// </summary>
public sealed class DiskUsageChart : Control
{
    private const double StartOffset = -Math.PI / 2; // 12 o'clock
    private const double TwoPi = Math.PI * 2;
    private const double MinSliceRadians = 0.015; // keep tiny non-zero categories visible (~0.85 deg)

    private readonly record struct DrawnSlice(DiskUsageCategory Category, double StartAngle, double EndAngle);
    private readonly List<DrawnSlice> _drawn = [];
    private Point _center;
    private double _innerRadius;
    private double _outerRadius;

    public static readonly StyledProperty<IReadOnlyList<DiskUsageSlice>?> SlicesProperty =
        AvaloniaProperty.Register<DiskUsageChart, IReadOnlyList<DiskUsageSlice>?>(nameof(Slices));

    public static readonly StyledProperty<DiskUsageCategory?> HighlightedCategoryProperty =
        AvaloniaProperty.Register<DiskUsageChart, DiskUsageCategory?>(
            nameof(HighlightedCategory),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<DiskUsageChart, IBrush?>(nameof(TrackBrush));

    static DiskUsageChart()
    {
        AffectsRender<DiskUsageChart>(SlicesProperty, HighlightedCategoryProperty, TrackBrushProperty);
    }

    public IReadOnlyList<DiskUsageSlice>? Slices
    {
        get => GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    public DiskUsageCategory? HighlightedCategory
    {
        get => GetValue(HighlightedCategoryProperty);
        set => SetValue(HighlightedCategoryProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsInfinity(availableSize.Width) ? 248 : Math.Min(availableSize.Width, 260);
        var h = double.IsInfinity(availableSize.Height) ? 248 : Math.Min(availableSize.Height, 260);
        var side = Math.Max(140, Math.Min(w, h));
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        _drawn.Clear();
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 24)
            return;

        _center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        _outerRadius = size / 2 - 10;
        var thickness = Math.Clamp(_outerRadius * 0.27, 22, 34);
        _innerRadius = Math.Max(12, _outerRadius - thickness);

        // Background track ring (visible while empty or as subtle separation between slices).
        if (TrackBrush is { } track)
        {
            var mid = (_outerRadius + _innerRadius) / 2;
            context.DrawEllipse(null, new Pen(track, thickness), _center, mid, mid);
        }

        var slices = Slices;
        if (slices is null || slices.Count == 0)
            return;

        var visible = new List<DiskUsageSlice>(slices.Count);
        foreach (var s in slices)
        {
            if (s.Fraction > 0)
                visible.Add(s);
        }
        if (visible.Count == 0)
            return;

        // Normalize angles while ensuring every non-zero slice has at least MinSliceRadians.
        var totalFraction = visible.Sum(s => s.Fraction);
        if (totalFraction <= 0)
            return;

        var rawAngles = new double[visible.Count];
        double totalRaw = 0;
        for (var i = 0; i < visible.Count; i++)
        {
            rawAngles[i] = Math.Max(MinSliceRadians, visible[i].Fraction / totalFraction * TwoPi);
            totalRaw += rawAngles[i];
        }
        var scale = TwoPi / totalRaw;

        var gap = visible.Count > 1 ? 0.016 : 0;
        var highlighted = HighlightedCategory;
        var cursor = 0.0;

        for (var i = 0; i < visible.Count; i++)
        {
            var slice = visible[i];
            var sweep = rawAngles[i] * scale;
            var start = cursor;
            var end = cursor + sweep;
            cursor = end;

            _drawn.Add(new(slice.Category, start, end));

            var isHot = highlighted == slice.Category;
            var outer = isHot ? _outerRadius + 5 : _outerRadius;
            var inner = isHot ? _innerRadius - 1 : _innerRadius;
            var dim = highlighted is not null && !isHot;

            var pad = visible.Count > 1 ? Math.Min(gap / 2, sweep * 0.22) : 0;
            var drawStart = start + pad;
            var drawSweep = Math.Max(0.002, sweep - pad * 2);

            using (dim ? context.PushOpacity(0.42) : default)
            {
                if (visible.Count == 1 || drawSweep >= TwoPi - 0.001)
                {
                    var mid = (outer + inner) / 2;
                    context.DrawEllipse(null, new Pen(slice.Brush, outer - inner), _center, mid, mid);
                }
                else
                {
                    var geometry = BuildArcGeometry(_center, inner, outer, StartOffset + drawStart, drawSweep);
                    context.DrawGeometry(slice.Brush, null, geometry);
                }
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTestSlice(e.GetPosition(this));
        if (HighlightedCategory != hit)
            HighlightedCategory = hit;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (HighlightedCategory is not null)
            HighlightedCategory = null;
    }

    private DiskUsageCategory? HitTestSlice(Point p)
    {
        if (_drawn.Count == 0)
            return null;

        var dx = p.X - _center.X;
        var dy = p.Y - _center.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < _innerRadius - 6 || dist > _outerRadius + 10)
            return null;

        var angle = Math.Atan2(dy, dx) - StartOffset;
        if (angle < 0)
            angle += TwoPi;
        else if (angle >= TwoPi)
            angle -= TwoPi;

        foreach (var s in _drawn)
        {
            if (angle >= s.StartAngle && angle <= s.EndAngle)
                return s.Category;
        }
        return _drawn[^1].Category;
    }

    private static StreamGeometry BuildArcGeometry(Point center, double innerR, double outerR, double startRad, double sweepRad)
    {
        var endRad = startRad + sweepRad;
        var largeArc = sweepRad > Math.PI;

        var outerStart = PointOnCircle(center, outerR, startRad);
        var outerEnd = PointOnCircle(center, outerR, endRad);
        var innerEnd = PointOnCircle(center, innerR, endRad);
        var innerStart = PointOnCircle(center, innerR, startRad);

        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(outerStart, isFilled: true);
        ctx.ArcTo(outerEnd, new Size(outerR, outerR), 0, largeArc, SweepDirection.Clockwise);
        ctx.LineTo(innerEnd);
        ctx.ArcTo(innerStart, new Size(innerR, innerR), 0, largeArc, SweepDirection.CounterClockwise);
        ctx.EndFigure(isClosed: true);
        return g;
    }

    private static Point PointOnCircle(Point center, double radius, double radians) =>
        new(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
}
