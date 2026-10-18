namespace Rift.Core.Operations;

public enum NameProblem
{
    None,
    Empty,
    InvalidCharacters,
}

public static class FileNames
{
    private static readonly char[] Invalid = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Checks a name typed by the user. Trailing dots and spaces are trimmed by <see cref="Clean"/>.</summary>
    public static NameProblem Validate(string name)
    {
        var clean = Clean(name);
        if (clean.Length == 0 || clean is "." or "..")
            return NameProblem.Empty;
        if (clean.IndexOfAny(Invalid) >= 0 || clean.Any(char.IsControl))
            return NameProblem.InvalidCharacters;
        if (Reserved.Contains(Path.GetFileNameWithoutExtension(clean)))
            return NameProblem.InvalidCharacters;
        return NameProblem.None;
    }

    /// <summary>Windows silently drops trailing dots and spaces; do it up front so the name we show is the real one.</summary>
    public static string Clean(string name) => name.Trim().TrimEnd('.', ' ');

    /// <summary>"report.pdf" → "report (2).pdf" → "report (3).pdf" until the name is free in <paramref name="directory"/>.</summary>
    public static string Unique(string directory, string name, bool isDirectory)
    {
        if (!Exists(Path.Combine(directory, name)))
            return name;

        var stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        var ext = isDirectory ? "" : Path.GetExtension(name);

        // "report (2)" → keep counting from 2 instead of producing "report (2) (2)".
        var open = stem.LastIndexOf(" (", StringComparison.Ordinal);
        if (open > 0 && stem.EndsWith(')') && int.TryParse(stem.AsSpan(open + 2, stem.Length - open - 3), out _))
            stem = stem[..open];

        for (var i = 2; ; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (!Exists(Path.Combine(directory, candidate)))
                return candidate;
        }
    }

    internal static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
