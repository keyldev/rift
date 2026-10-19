using System.Text;

namespace Rift.Core;

/// <summary>
/// Rift's log: one file a day in %AppData%\Rift\logs, the last week kept. Meant for what went wrong
/// (a failed shell call, a settings file that didn't save, a crash), not for tracing every click,
/// so writes are synchronous: a line that's written survives the crash that follows it.
/// </summary>
public static class Log
{
    private const int KeepDays = 7;
    /// <summary>A runaway loop of errors stops here instead of filling the disk.</summary>
    private const long MaxFileBytes = 4 * 1024 * 1024;
    private const int RecentLines = 200;

    private static readonly Lock Gate = new();
    private static readonly Queue<string> Recent = new();
    private static bool _cleaned;
    private static string? _fullFile;

    public static string Folder { get; set; } = Path.Combine(SessionStore.Folder, "logs");

    /// <summary>Today's file.</summary>
    public static string CurrentFile => Path.Combine(Folder, $"rift-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INF", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WRN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERR", message, ex);

    /// <summary>The last lines written in this run, oldest first: they go into a crash report.</summary>
    public static IReadOnlyList<string> RecentEntries()
    {
        lock (Gate)
            return [.. Recent];
    }

    private static void Write(string level, string message, Exception? ex)
    {
        var text = new StringBuilder($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
        if (ex is not null)
            text.AppendLine().Append("    ").Append(ex.ToString().Replace("\n", "\n    "));
        var entry = text.ToString();

        lock (Gate)
        {
            Recent.Enqueue(entry);
            if (Recent.Count > RecentLines)
                Recent.Dequeue();
            try
            {
                Directory.CreateDirectory(Folder);
                if (!_cleaned)
                {
                    _cleaned = true;
                    DeleteOld();
                }
                var file = CurrentFile;
                if (_fullFile == file)
                    return;
                if (File.Exists(file) && new FileInfo(file).Length > MaxFileBytes)
                {
                    _fullFile = file;
                    File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [WRN] The log is full for today; nothing more is written.{Environment.NewLine}");
                    return;
                }
                File.AppendAllText(file, entry + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Nowhere to complain to; the entry is still in Recent for a crash report.
            }
        }
    }

    private static void DeleteOld()
    {
        var cutoff = DateTime.Now.Date.AddDays(-KeepDays);
        foreach (var file in Directory.EnumerateFiles(Folder))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Open in an editor, probably; next time.
            }
        }
    }
}
