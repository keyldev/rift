namespace Rift.Core;

/// <summary>
/// Command line: <c>Rift [path...] [--left path]... [--right path]... [--lang en|ru] [--new-window]</c>.
/// Every path becomes a tab; bare paths go to the left pane.
/// <c>--new-window</c> opens a separate window instead of handing the paths to the running one.
/// </summary>
public sealed record StartupArgs(IReadOnlyList<string> Left, IReadOnlyList<string> Right, string? Language = null, bool NewWindow = false)
{
    public bool HasPaths => Left.Count > 0 || Right.Count > 0;

    /// <summary>Arguments for the running instance: absolute paths, so its working folder doesn't matter.</summary>
    public IReadOnlyList<string> ToForwardedArgs() =>
        [.. Left.SelectMany(p => new[] { "--left", p }), .. Right.SelectMany(p => new[] { "--right", p })];

    public static StartupArgs Parse(IEnumerable<string> args)
    {
        var left = new List<string>();
        var right = new List<string>();
        string? language = null;
        var newWindow = false;
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
                case "--new-window":
                    newWindow = true;
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

            (pendingSide ? target : left).Add(Locations.KindOf(path) switch
            {
                LocationKind.ThisPc => Locations.ThisPc,
                LocationKind.RecycleBin => Locations.RecycleBin,
                LocationKind.Network => Locations.Network,
                LocationKind.Libraries => Locations.LibrariesFolder,
                LocationKind.Server => path.TrimEnd('\\'),
                _ => Path.GetFullPath(path),
            });
            pendingSide = false;
        }

        return new StartupArgs(left, right, language, newWindow);
    }
}
