using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Rift.App.ViewModels;

namespace Rift.App.Views;

public partial class DiskUsageView : UserControl
{
    public DiskUsageView()
    {
        InitializeComponent();

        // Keep keys inside the overlay so shortcuts like Delete cannot reach the file list behind it.
        AddHandler(KeyDownEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble);

        Scrim.PointerPressed += (_, e) =>
        {
            if (e.Source == Scrim)
                Vm?.CloseCommand.Execute(null);
        };
    }

    private DiskUsageViewModel? Vm => DataContext as DiskUsageViewModel;

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => CloseButton.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OnRowPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: DiskUsageRow row })
            Vm?.SetHoveredCategory(row.Category);
    }

    private void OnRowPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: DiskUsageRow row } && Vm?.HighlightedCategory == row.Category)
            Vm.SetHoveredCategory(null);
    }
}
