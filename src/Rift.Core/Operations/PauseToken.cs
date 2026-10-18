namespace Rift.Core.Operations;

/// <summary>Lets the UI pause a running operation between chunks.</summary>
public sealed class PauseTokenSource
{
    private TaskCompletionSource? _paused;

    public bool IsPaused => _paused is not null;

    public PauseToken Token => new(this);

    public void Pause() => Interlocked.CompareExchange(ref _paused, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), null);

    public void Resume() => Interlocked.Exchange(ref _paused, null)?.TrySetResult();

    internal Task WaitAsync(CancellationToken ct) => _paused?.Task.WaitAsync(ct) ?? Task.CompletedTask;
}

public readonly struct PauseToken(PauseTokenSource? source)
{
    public Task WaitWhilePausedAsync(CancellationToken ct) => source?.WaitAsync(ct) ?? Task.CompletedTask;
}
