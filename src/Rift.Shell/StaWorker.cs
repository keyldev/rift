using System.Collections.Concurrent;
using System.Runtime.Versioning;

namespace Rift.Shell;

/// <summary>
/// A background thread in a single-threaded apartment: shell COM objects (icons, jump lists)
/// want STA, and the UI thread must stay free.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StaWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public StaWorker(string name)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> Invoke<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() =>
            {
                try
                {
                    tcs.SetResult(work());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
        }
        catch (InvalidOperationException)
        {
            tcs.SetCanceled();
        }
        return tcs.Task;
    }

    public Task Invoke(Action work) => Invoke(() =>
    {
        work();
        return true;
    });

    private void Run()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
            work();
    }

    public void Dispose() => _queue.CompleteAdding();
}
