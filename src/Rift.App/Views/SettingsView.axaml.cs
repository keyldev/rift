using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Rift.App.ViewModels;

namespace Rift.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        // Shortcut recording and Esc are handled by the window, which sees every key first.
        // Keys typed here (tag names) must not reach the window's shortcuts: Delete would delete files.
        AddHandler(KeyDownEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble);

        // A click on the dimmed area outside the card closes Settings.
        Scrim.PointerPressed += (_, e) =>
        {
            if (e.Source == Scrim)
                Vm?.CloseCommand.Execute(null);
        };
    }

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    /// <summary>Focus moves in when Settings open, so Esc works right away.</summary>
    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => CloseButton.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OnTagNameLostFocus(object? sender, RoutedEventArgs e) => Vm?.SaveTags();
}
