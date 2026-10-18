namespace Rift.Core;

/// <summary>Back/forward history of a single tab.</summary>
public sealed class NavigationHistory(string start)
{
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();

    public string Current { get; private set; } = start;
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    public void Push(string path)
    {
        if (string.Equals(path, Current, StringComparison.OrdinalIgnoreCase))
            return;
        _back.Push(Current);
        _forward.Clear();
        Current = path;
    }

    public string? Back()
    {
        if (!CanGoBack) return null;
        _forward.Push(Current);
        Current = _back.Pop();
        return Current;
    }

    public string? Forward()
    {
        if (!CanGoForward) return null;
        _back.Push(Current);
        Current = _forward.Pop();
        return Current;
    }
}
