using System.Diagnostics;
using Avalonia.Threading;

namespace Rift.App.Services;

/// <summary>
/// Timings for checking that big folders and slow drives don't freeze the window. Off unless the
/// RIFT_PERF environment variable is 1; then folder loads and every UI-thread stall over 50 ms
/// go to %TEMP%\rift-perf.log.
/// </summary>
public static class Perf
{
    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("RIFT_PERF") == "1";

    private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "rift-perf.log");
    private static readonly Lock Gate = new();
    private static readonly TimeSpan StallThreshold = TimeSpan.FromMilliseconds(50);

    public static void Log(string message)
    {
        if (!Enabled)
            return;
        lock (Gate)
            File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
    }

    /// <summary>Starts a stopwatch only when timing is on, so normal runs pay nothing.</summary>
    public static Stopwatch? Start() => Enabled ? Stopwatch.StartNew() : null;

    /// <summary>
    /// Pings the UI thread from a background thread every 20 ms and logs when an answer is late:
    /// that's how long a click or a key press would have waited.
    /// </summary>
    public static void WatchUiThread()
    {
        if (!Enabled)
            return;
        Log("--- started");
        var thread = new Thread(() =>
        {
            using var answered = new ManualResetEventSlim();
            while (true)
            {
                answered.Reset();
                var sent = Stopwatch.StartNew();
                Dispatcher.UIThread.Post(answered.Set, DispatcherPriority.Input);
                if (!answered.Wait(StallThreshold))
                {
                    answered.Wait();
                    Log($"UI stall {sent.ElapsedMilliseconds} ms");
                }
                Thread.Sleep(20);
            }
        }) { IsBackground = true, Name = "Rift stall watch" };
        thread.Start();
    }
}
