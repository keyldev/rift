using System.Text.RegularExpressions;

namespace Rift.Core;

/// <summary>Exception text that's safe to show publicly: types and frames, no messages.</summary>
public static partial class StackTraces
{
    /// <summary>
    /// "System.IO.IOException: The file 'C:\Users\…' is in use" → "System.IO.IOException". Messages often
    /// carry file paths and names; the stack frames below them don't.
    /// </summary>
    public static string WithoutMessage(string line) => ExceptionLine().Replace(line, "$1");

    /// <summary>
    /// A GitHub issue for a crash report (as <c>CrashReports</c> writes it: a header, a blank line, the exception,
    /// "Last log entries:"): the header and the stack without messages. The log lines stay out, they name files.
    /// </summary>
    public static (string Title, string Body) Issue(IReadOnlyList<string> report, string fileName)
    {
        var header = report.TakeWhile(l => l.Length > 0).ToList();
        var trace = report.Skip(header.Count + 1).TakeWhile(l => l != "Last log entries:").Select(WithoutMessage).ToList();
        var title = $"Crash: {trace.FirstOrDefault()?.Trim() ?? "exception"}";

        var body = new System.Text.StringBuilder()
            .AppendLine("**What I was doing:** ")
            .AppendLine()
            .AppendLine(string.Join("  \n", header))
            .AppendLine()
            .AppendLine("```")
            .AppendLine(string.Join("\n", trace).Trim())
            .AppendLine("```")
            .AppendLine()
            .Append($"The full report is `{fileName}` in the logs folder; attach it if nothing in it is private.")
            .ToString();
        // Browsers and GitHub cut long links; the top of the trace is what matters.
        if (body.Length > 5000)
            body = body[..5000] + "\n```\n(cut)";
        return (title, body);
    }

    [GeneratedRegex(@"^(\s*(?:---> )?[\w.`+]+(?:Exception|Error)\b[\w`]*):.*$")]
    private static partial Regex ExceptionLine();
}
