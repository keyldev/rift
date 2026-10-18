using CommunityToolkit.Mvvm.ComponentModel;
using Rift.Core;

namespace Rift.App.ViewModels;

/// <summary>Which detail columns are shown and how wide they are; shared by both panes so they line up.</summary>
public sealed partial class ColumnLayout : ObservableObject
{
    public const double MinWidth = 48;

    [ObservableProperty] private bool _showModified = true;
    [ObservableProperty] private bool _showCreated;
    [ObservableProperty] private bool _showType = true;
    [ObservableProperty] private bool _showSize = true;

    [ObservableProperty] private double _modifiedWidth = 130;
    [ObservableProperty] private double _createdWidth = 130;
    [ObservableProperty] private double _typeWidth = 110;
    [ObservableProperty] private double _sizeWidth = 80;

    /// <summary>Drag on a column's left edge: moving left widens it (the name column gives up the space).</summary>
    public void Resize(SortColumn column, double delta)
    {
        switch (column)
        {
            case SortColumn.Modified: ModifiedWidth = Math.Max(MinWidth, ModifiedWidth + delta); break;
            case SortColumn.Created: CreatedWidth = Math.Max(MinWidth, CreatedWidth + delta); break;
            case SortColumn.Type: TypeWidth = Math.Max(MinWidth, TypeWidth + delta); break;
            case SortColumn.Size: SizeWidth = Math.Max(MinWidth, SizeWidth + delta); break;
        }
    }

    public void Toggle(SortColumn column)
    {
        switch (column)
        {
            case SortColumn.Modified: ShowModified = !ShowModified; break;
            case SortColumn.Created: ShowCreated = !ShowCreated; break;
            case SortColumn.Type: ShowType = !ShowType; break;
            case SortColumn.Size: ShowSize = !ShowSize; break;
        }
    }

    public bool IsShown(SortColumn column) => column switch
    {
        SortColumn.Modified => ShowModified,
        SortColumn.Created => ShowCreated,
        SortColumn.Type => ShowType,
        SortColumn.Size => ShowSize,
        _ => true,
    };
}
