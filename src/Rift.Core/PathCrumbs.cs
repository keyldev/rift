namespace Rift.Core;

public sealed record PathCrumb(string Label, string Path);

public static class PathCrumbs
{
    /// <summary>
    /// Splits a Windows path into clickable segments.
    /// <c>C:\Users\keyl</c> → <c>C:</c>, <c>Users</c>, <c>keyl</c>;
    /// <c>\\nas\media\movies</c> → <c>\\nas\media</c>, <c>movies</c> (the share is the root).
    /// </summary>
    public static IReadOnlyList<PathCrumb> Split(string path)
    {
        var crumbs = new List<PathCrumb>();
        if (string.IsNullOrWhiteSpace(path))
            return crumbs;

        var trimmed = path.TrimEnd('\\', '/');
        string root;
        string rest;

        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = trimmed[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            var shareParts = Math.Min(2, parts.Length);
            root = @"\\" + string.Join('\\', parts.Take(shareParts));
            rest = string.Join('\\', parts.Skip(shareParts));
            crumbs.Add(new PathCrumb(root, root + '\\'));
        }
        else
        {
            var sep = trimmed.IndexOf('\\');
            root = sep < 0 ? trimmed : trimmed[..sep];
            rest = sep < 0 ? "" : trimmed[(sep + 1)..];
            crumbs.Add(new PathCrumb(root, root + '\\'));
        }

        var current = root;
        foreach (var part in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current + '\\' + part;
            crumbs.Add(new PathCrumb(part, current));
        }

        return crumbs;
    }

    /// <summary>Last segment for tab titles: "Downloads", "C:", "\\nas\media".</summary>
    public static string Title(string path)
    {
        var crumbs = Split(path);
        return crumbs.Count == 0 ? path : crumbs[^1].Label;
    }

    public static string? Parent(string path)
    {
        var crumbs = Split(path);
        return crumbs.Count > 1 ? crumbs[^2].Path : null;
    }
}
