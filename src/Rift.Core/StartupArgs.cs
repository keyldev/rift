namespace Rift.Core;

/// <summary>
/// Command line: <c>Rift [path...] [--left path]... [--right path]... [--lang en|ru]</c>.
/// Every path becomes a tab; bare paths go to the left pane.
/// </summary>
public sealed record StartupArgs(IReadOnlyList<string> Left, IReadOnlyList<string> Right, string? Language = null)
{
    public static StartupArgs Parse(IEnumerable<string> args)
    {
        var left = new List<string>();
        var right = new List<string>();
        string? language = null;
        var target = left;
        var pendingSide = false;
        var pendingLanguage = false;

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--left":
                    target = left;
                    pendingSide = true;
                    continue;
                case "--right":
                    target = right;
                    pendingSide = true;
                    continue;
                case "--lang":
                    pendingLanguage = true;
                    continue;
            }

            if (pendingLanguage)
            {
                language = arg.Trim().ToLowerInvariant();
                pendingLanguage = false;
                continue;
            }

            // Explorer passes drive roots as "C:\" — the trailing \" turns into a stray quote.
            var path = arg.Trim().TrimEnd('"');
            if (path.Length == 0)
                continue;
            // A bare "C:" means "current folder on drive C", not its root.
            if (path is [_, ':'])
                path += '\\';

            (pendingSide ? target : left).Add(Path.GetFullPath(path));
            pendingSide = false;
        }

        return new StartupArgs(left, right, language);
    }
}
