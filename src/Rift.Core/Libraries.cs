using System.Xml.Linq;

namespace Rift.Core;

/// <summary>A Windows library (Documents, Pictures, …): a name over several real folders.</summary>
public sealed record LibraryInfo(string File, string Name, IReadOnlyList<string> Folders);

/// <summary>Reads .library-ms files, the XML Explorer keeps in %AppData%\Microsoft\Windows\Libraries.</summary>
public static class Libraries
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2009/library";

    /// <param name="knownFolder">Resolves "knownfolder:{GUID}" locations to a path (shell API, so passed in).</param>
    /// <param name="indirectName">Resolves "@shell32.dll,-34575" style names to text.</param>
    public static LibraryInfo? Parse(string file, string xml, Func<Guid, string?> knownFolder, Func<string, string?> indirectName)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
        if (doc.Root is null)
            return null;

        var rawName = doc.Root.Attribute("name")?.Value ?? doc.Root.Element(Ns + "name")?.Value;
        var name = rawName is { Length: > 0 } && rawName[0] == '@' ? indirectName(rawName) : rawName;
        if (string.IsNullOrWhiteSpace(name))
            name = Path.GetFileNameWithoutExtension(file);

        var folders = new List<string>();
        foreach (var url in doc.Descendants(Ns + "url").Select(u => u.Value.Trim()))
        {
            string? path = url.StartsWith("knownfolder:", StringComparison.OrdinalIgnoreCase)
                ? Guid.TryParse(url["knownfolder:".Length..], out var id) ? knownFolder(id) : null
                : url;
            if (!string.IsNullOrEmpty(path) && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                folders.Add(path);
        }
        return new LibraryInfo(file, name, folders);
    }

    /// <summary>A library's contents: every folder's items side by side. Missing folders are skipped.</summary>
    public static IReadOnlyList<FileEntry> ReadContents(LibraryInfo library, bool includeHidden)
    {
        var entries = new List<FileEntry>();
        foreach (var folder in library.Folders)
        {
            try
            {
                entries.AddRange(DirectoryReader.Read(folder, includeHidden));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An offline drive or a removed folder shouldn't hide the rest.
            }
        }
        return entries;
    }
}
