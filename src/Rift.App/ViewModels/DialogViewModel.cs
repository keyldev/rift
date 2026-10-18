using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Rift.App.ViewModels;

public sealed record DialogButton(string Label, int Result, bool IsPrimary = false, bool IsDanger = false);

public readonly record struct DialogResult(int Button, bool IsChecked, string? Input = null);

/// <summary>
/// A modal card drawn over the window: conflicts, delete confirmation, errors.
/// Enter picks the primary button, Esc the last one (by convention "Cancel").
/// </summary>
public sealed partial class DialogViewModel : ViewModelBase
{
    private readonly TaskCompletionSource<DialogResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public required string Title { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<string> Details { get; init; } = [];
    public required IReadOnlyList<DialogButton> Buttons { get; init; }
    public string? CheckBoxText { get; init; }
    public bool HasCheckBox => CheckBoxText is not null;
    public bool HasMessage => Message is not null;

    [ObservableProperty] private bool _isChecked;

    /// <summary>A text box under the message (e.g. a workspace name); null for no box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInput))]
    private string? _input;

    public bool HasInput => Input is not null;

    public Task<DialogResult> Result => _result.Task;

    [RelayCommand]
    public void Choose(DialogButton button) => _result.TrySetResult(new DialogResult(button.Result, IsChecked, Input));

    public void ChoosePrimary() => Choose(Buttons.FirstOrDefault(b => b.IsPrimary) ?? Buttons[0]);

    public void ChooseCancel() => Choose(Buttons[^1]);

    public static DialogViewModel Error(string title, string message, string ok) => new()
    {
        Title = title,
        Message = message,
        Buttons = [new DialogButton(ok, 0, IsPrimary: true)],
    };
}
