using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Rift.Core;

namespace Rift.App.Services;

/// <summary>The running version, without the commit hash the SDK appends.</summary>
public static class AppInfo
{
    public static string Version { get; } =
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?")
        .Split('+')[0];
}

/// <summary>A crash report found on disk, from an earlier run.</summary>
public sealed record CrashReport(string File, DateTime Time);

/// <summary>
/// When Rift dies on an exception, a crash-*.txt goes next to the logs: version, Windows, the exception
/// and the last log lines. The next start offers to report it on GitHub (see <see cref="IssueUrl"/>).
/// </summary>
public static class CrashReports
{
    private const string Issues = "https://github.com/keyldev/rift/issues/new";
    private static int _written;

    /// <summary>Hooks the last-resort handlers; call first thing in Main.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Write(ex);
        };
        // Not fatal since .NET 4.5, but a task that failed with nobody awaiting it is a bug worth knowing about.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Warn("A background task failed and nothing was waiting for it", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Writes the report; once per run, the first exception is the one that matters.</summary>
    /// <returns>The report's path, or null if it couldn't be written or one already was.</returns>
    public static string? Write(Exception ex)
    {
        if (Interlocked.Exchange(ref _written, 1) == 1)
            return null;
        Log.Error("Rift crashed", ex);
        try
        {
            var started = Process.GetCurrentProcess().StartTime;
            var text = new StringBuilder()
                .AppendLine($"Rift {AppInfo.Version}")
                .AppendLine($"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})")
                .AppendLine($"{RuntimeInformation.FrameworkDescription}, {(RuntimeFeature.IsDynamicCodeCompiled ? "JIT" : "Native AOT")}")
                .AppendLine($"Started {started:yyyy-MM-dd HH:mm:ss}, crashed after {DateTime.Now - started:hh\\:mm\\:ss}")
                .AppendLine()
                .AppendLine(ex.ToString())
                .AppendLine()
                .AppendLine("Last log entries:");
            foreach (var entry in Log.RecentEntries())
                text.AppendLine(entry);

            Directory.CreateDirectory(Log.Folder);
            var file = Path.Combine(Log.Folder, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(file, text.ToString());
            return file;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The newest report written after <paramref name="since"/>, if any.</summary>
    public static CrashReport? Latest(DateTime? since)
    {
        try
        {
            if (!Directory.Exists(Log.Folder))
                return null;
            return Directory.EnumerateFiles(Log.Folder, "crash-*.txt")
                .Select(f => new CrashReport(f, File.GetLastWriteTime(f)))
                .Where(r => since is null || r.Time > since)
                .MaxBy(r => r.Time);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// A "new issue" link with the version, Windows and the stack trace filled in. Exception messages are
    /// left out: they often carry file paths. The user sees everything before sending and can attach the full report.
    /// </summary>
    public static string IssueUrl(CrashReport report)
    {
        var (title, body) = StackTraces.Issue(File.ReadAllLines(report.File), Path.GetFileName(report.File));
        return $"{Issues}?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
    }
}
