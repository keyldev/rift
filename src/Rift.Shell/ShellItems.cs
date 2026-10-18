using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace Rift.Shell;

/// <summary>
/// Files as the shell sees them: a parent folder plus child item IDs, which is what context menus,
/// property sheets and data objects are built from.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class ShellItems : IDisposable
{
    private readonly List<nint> _absolute = [];

    public IShellFolder Parent { get; }
    /// <summary>Child IDs relative to <see cref="Parent"/>; they point into the absolute IDs we own.</summary>
    public nint[] Children { get; }

    private ShellItems(IShellFolder parent, nint[] children, List<nint> absolute)
    {
        Parent = parent;
        Children = children;
        _absolute = absolute;
    }

    /// <summary>All paths must live in the same folder (they do: they come from one pane).</summary>
    public static ShellItems? FromPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return null;

        var absolute = new List<nint>();
        var children = new nint[paths.Count];
        IShellFolder? parent = null;
        try
        {
            for (var i = 0; i < paths.Count; i++)
            {
                if (SHParseDisplayName(paths[i], 0, out var pidl, 0, out _) != Com.S_OK)
                    throw new FileNotFoundException(paths[i]);
                absolute.Add(pidl);
                if (i == 0)
                {
                    var iid = typeof(IShellFolder).GUID;
                    Marshal.ThrowExceptionForHR(SHBindToParent(pidl, iid, out var folder, out children[0]));
                    parent = Com.Wrap<IShellFolder>(folder);
                }
                else
                {
                    children[i] = ILFindLastID(pidl);
                }
            }
            return new ShellItems(parent!, children, absolute);
        }
        catch
        {
            foreach (var p in absolute)
                Marshal.FreeCoTaskMem(p);
            Com.Free(parent);
            throw;
        }
    }

    /// <summary>The folder itself, for menus on empty space inside it.</summary>
    public static IShellFolder FolderFromPath(string path)
    {
        if (SHParseDisplayName(path, 0, out var pidl, 0, out _) != Com.S_OK)
            throw new DirectoryNotFoundException(path);
        try
        {
            var iid = typeof(IShellFolder).GUID;
            Marshal.ThrowExceptionForHR(SHBindToObject(0, pidl, 0, iid, out var folder));
            return Com.Wrap<IShellFolder>(folder);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>Asks the parent folder for an interface over the children (IContextMenu, IDataObject).</summary>
    public nint GetUIObject(nint owner, Guid iid)
    {
        fixed (nint* children = Children)
        {
            Marshal.ThrowExceptionForHR(Parent.GetUIObjectOf(owner, (uint)Children.Length, children, iid, 0, out var ptr));
            return ptr;
        }
    }

    public void Dispose()
    {
        foreach (var p in _absolute)
            Marshal.FreeCoTaskMem(p);
        _absolute.Clear();
        Com.Free(Parent);
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributesIn, out uint attributesOut);

    [LibraryImport("shell32.dll")]
    private static partial int SHBindToParent(nint pidl, in Guid riid, out nint ppv, out nint pidlLast);

    [LibraryImport("shell32.dll")]
    private static partial int SHBindToObject(nint folder, nint pidl, nint bindContext, in Guid riid, out nint ppv);

    [LibraryImport("shell32.dll")]
    private static partial nint ILFindLastID(nint pidl);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214E6-0000-0000-C000-000000000046")]
internal unsafe partial interface IShellFolder
{
    [PreserveSig] int ParseDisplayName(nint hwnd, nint bindContext, string displayName, out uint eaten, out nint pidl, ref uint attributes);
    [PreserveSig] int EnumObjects(nint hwnd, uint flags, out nint enumIdList);
    [PreserveSig] int BindToObject(nint pidl, nint bindContext, in Guid riid, out nint ppv);
    [PreserveSig] int BindToStorage(nint pidl, nint bindContext, in Guid riid, out nint ppv);
    [PreserveSig] int CompareIDs(nint lParam, nint pidl1, nint pidl2);
    [PreserveSig] int CreateViewObject(nint hwndOwner, in Guid riid, out nint ppv);
    [PreserveSig] int GetAttributesOf(uint count, nint* pidls, ref uint attributes);
    [PreserveSig] int GetUIObjectOf(nint hwndOwner, uint count, nint* pidls, in Guid riid, nint reserved, out nint ppv);
    [PreserveSig] int GetDisplayNameOf(nint pidl, uint flags, nint strret);
    [PreserveSig] int SetNameOf(nint hwnd, nint pidl, string name, uint flags, out nint pidlOut);
}
