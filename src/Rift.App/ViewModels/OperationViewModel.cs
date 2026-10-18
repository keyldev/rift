using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rift.Core;
using Rift.Core.Localization;
using Rift.Core.Operations;

namespace Rift.App.ViewModels;

public enum OperationState
{
    Running,
    Paused,
    Done,
    Canceled,
    Failed,
}

/// <summary>One card in the operations panel (bottom right): progress, pause, cancel.</summary>
public sealed partial class OperationViewModel : ViewModelBase
{
    private readonly CancellationTokenSource _cts = new();
    private readonly PauseTokenSource _pause = new();
    private readonly Action<OperationViewModel> _remove;

    public OperationViewModel(string title, bool canPause, Action<OperationViewModel> remove)
    {
        Title = title;
        CanPause = canPause;
        _remove = remove;
    }

    public string Title { get; }
    public bool CanPause { get; }
    public CancellationToken Token => _cts.Token;
    public PauseToken PauseToken => _pause.Token;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(IsFinished), nameof(IsPaused), nameof(ShowPause), nameof(StateText))]
    private OperationState _state = OperationState.Running;

    [ObservableProperty] private double _percent;
    [ObservableProperty] private bool _isIndeterminate = true;
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private string? _errorText;

    public bool IsActive => State is OperationState.Running or OperationState.Paused;
    public bool IsFinished => !IsActive;
    public bool IsPaused => State == OperationState.Paused;
    public bool ShowPause => CanPause && IsActive;

    public string StateText => State switch
    {
        OperationState.Paused => Strings.OpPaused,
        OperationState.Done => Strings.OpDone,
        OperationState.Canceled => Strings.OpCanceled,
        OperationState.Failed => Strings.OpFailed,
        _ => "",
    };

    public void Update(TransferProgress p)
    {
        IsIndeterminate = p.BytesTotal == 0 && p.ItemsTotal == 0;
        Percent = p.Fraction * 100;
        Detail = p.BytesTotal > 0
            ? Strings.OpBytes(SizeFormatter.Format(p.BytesDone), SizeFormatter.Format(p.BytesTotal))
            : p.ItemsTotal > 0 ? $"{p.ItemsDone} / {p.ItemsTotal}" : "";
    }

    public void Complete(bool canceled, IReadOnlyList<OperationError> errors)
    {
        IsIndeterminate = false;
        if (errors.Count > 0)
        {
            State = OperationState.Failed;
            var lines = errors.Take(3).Select(e => $"{Path.GetFileName(e.Path)}: {e.Message}").ToList();
            if (errors.Count > 3)
                lines.Add(Strings.OpErrorsMore(errors.Count - 3));
            ErrorText = Strings.OpErrors(errors.Count) + "\n" + string.Join("\n", lines);
            return;
        }

        State = canceled ? OperationState.Canceled : OperationState.Done;
        Percent = canceled ? Percent : 100;
        // Successful cards fade away on their own; failures stay until dismissed.
        DispatcherTimer.RunOnce(() => _remove(this), TimeSpan.FromSeconds(canceled ? 1.5 : 2.5));
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (State == OperationState.Paused)
        {
            _pause.Resume();
            State = OperationState.Running;
        }
        else if (State == OperationState.Running)
        {
            _pause.Pause();
            State = OperationState.Paused;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _pause.Resume();
        _cts.Cancel();
    }

    [RelayCommand]
    private void Dismiss() => _remove(this);
}

public sealed class OperationsViewModel
{
    public ObservableCollection<OperationViewModel> Items { get; } = [];

    public OperationViewModel Start(string title, bool canPause = true)
    {
        var op = new OperationViewModel(title, canPause, o => Items.Remove(o));
        Items.Add(op);
        return op;
    }
}
