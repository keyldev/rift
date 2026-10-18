using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Rift.App.ViewModels;

namespace Rift.App.Controls;

/// <summary>
/// Inline rename field. When it appears it takes focus and selects the name without the
/// extension, like Explorer, so typing replaces "report" but keeps ".pdf".
/// </summary>
public sealed class RenameBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
            Dispatcher.UIThread.Post(SelectStem, DispatcherPriority.Input);
    }

    private void SelectStem()
    {
        Focus();
        var text = Text ?? "";
        var dot = text.LastIndexOf('.');
        SelectionStart = 0;
        SelectionEnd = dot > 0 && DataContext is FileItem { IsDirectory: false } ? dot : text.Length;
    }
}
