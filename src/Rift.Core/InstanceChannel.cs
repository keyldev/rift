using System.IO.Pipes;

namespace Rift.Core;

/// <summary>
/// Keeps Rift to one window: a second Rift.exe hands its arguments to the running one and exits.
/// "Open in Rift" from Explorer and jump list items then become new tabs in the existing window.
/// </summary>
public sealed class InstanceChannel : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();

    private InstanceChannel(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    /// <summary>Per user and per logon session, so two users on one machine don't share a window.</summary>
    public static string DefaultName =>
        $"Rift-{Environment.UserName}-{System.Diagnostics.Process.GetCurrentProcess().SessionId}";

    /// <summary>
    /// Becomes the primary instance, or returns null if another one already runs
    /// (call <see cref="SendAsync"/> to hand the arguments over).
    /// </summary>
    public static InstanceChannel? TryClaim(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{name}", out var createdNew);
        if (createdNew)
            return new InstanceChannel(mutex, name);
        mutex.Dispose();
        return null;
    }

    /// <summary>Sends arguments to the primary instance. Waits a little: it may still be starting up.</summary>
    public static async Task<bool> SendAsync(string name, IReadOnlyList<string> args, TimeSpan timeout)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(timeout);
            await pipe.ConnectAsync(cts.Token);
            await using var writer = new BinaryWriter(pipe);
            writer.Write(args.Count);
            foreach (var arg in args)
                writer.Write(arg);
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Starts accepting arguments from later instances; <paramref name="received"/> runs on a pool thread.</summary>
    public void Listen(Action<IReadOnlyList<string>> received) => _ = ListenAsync(received, _stop.Token);

    private async Task ListenAsync(Action<IReadOnlyList<string>> received, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // CurrentUserOnly: another account can't push paths into this window.
                await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct);
                var args = await Task.Run(() => Read(pipe), ct);
                if (args is not null)
                    received(args);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException or UnauthorizedAccessException)
            {
                // A client that dies mid-message must not take the listener down.
            }
        }
    }

    private static List<string>? Read(Stream pipe)
    {
        using var reader = new BinaryReader(pipe);
        var count = reader.ReadInt32();
        if (count is < 0 or > 1000)
            return null;
        var args = new List<string>(count);
        for (var i = 0; i < count; i++)
            args.Add(reader.ReadString());
        return args;
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from another thread than the one that claimed it; closing the handle is enough.
        }
        _mutex.Dispose();
    }
}
