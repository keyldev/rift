using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Rift.Core;

namespace Rift.App.ViewModels;

/// <summary>
/// Which columns one pane actually shows: the shared choice (<see cref="ColumnLayout"/>) minus what doesn't fit.
/// A narrow pane drops Created first, then Type, then Modified, so the name always keeps room to read.
/// </summary>
public sealed partial class PaneColumns : ObservableObject
{
    private const double NameMinWidth = 120;
    private static readonly SortColumn[] DropOrder = [SortColumn.Created, SortColumn.Type, SortColumn.Modified];

    private readonly ColumnLayout _layout = ViewOptions.Current.Columns;
    private double _width = double.PositiveInfinity;

    public PaneColumns()
    {
        // Weak enough for our needs: panes live as long as the window.
        _layout.PropertyChanged += (_, _) => Update();
        Update();
    }

    [ObservableProperty] private bool _modified;
    [ObservableProperty] private bool _created;
    [ObservableProperty] private bool _type;
    [ObservableProperty] private bool _size;

    /// <summary>The file list's width changed.</summary>
    public void Fit(double width)
    {
        if (Math.Abs(width - _width) < 1)
            return;
        _width = width;
        Update();
    }

    private void Update()
    {
        var shown = new HashSet<SortColumn>(new[] { SortColumn.Modified, SortColumn.Created, SortColumn.Type, SortColumn.Size }
            .Where(_layout.IsShown));
        foreach (var column in DropOrder)
        {
            if (Needed(shown) <= _width)
                break;
            shown.Remove(column);
        }
        Modified = shown.Contains(SortColumn.Modified);
        Created = shown.Contains(SortColumn.Created);
        Type = shown.Contains(SortColumn.Type);
        Size = shown.Contains(SortColumn.Size);
    }

    private double Needed(HashSet<SortColumn> shown) => NameMinWidth + shown.Sum(c => c switch
    {
        SortColumn.Modified => _layout.ModifiedWidth,
        SortColumn.Created => _layout.CreatedWidth,
        SortColumn.Type => _layout.TypeWidth,
        SortColumn.Size => _layout.SizeWidth,
        _ => 0,
    });
}
