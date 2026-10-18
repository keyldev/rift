using System.Text.RegularExpressions;

namespace Rift.Core;

/// <summary>
/// Matches file names against what the user typed: a plain substring ("report"),
/// or a wildcard pattern when it has * or ? ("*.pdf", "IMG_20??*"). Always case-insensitive.
/// </summary>
public sealed class NameFilter
{
    private readonly string _text;
    private readonly Regex? _pattern;

    public NameFilter(string text)
    {
        _text = text.Trim();
        if (_text.IndexOfAny(['*', '?']) >= 0)
        {
            var regex = "^" + Regex.Escape(_text).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            _pattern = new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }

    public bool IsEmpty => _text.Length == 0;

    public bool Matches(string name) =>
        IsEmpty || (_pattern?.IsMatch(name) ?? name.Contains(_text, StringComparison.OrdinalIgnoreCase));
}
