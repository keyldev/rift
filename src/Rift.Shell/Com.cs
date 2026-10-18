using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Rift.Shell;

/// <summary>COM plumbing for the shell interfaces: source-generated wrappers, so it stays trim- and AOT-friendly.</summary>
internal static partial class Com
{
    public static readonly StrategyBasedComWrappers Wrappers = new();

    public const int S_OK = 0;
    public const uint CLSCTX_INPROC_SERVER = 1;

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    /// <summary>Creates a COM object and wraps it as <typeparamref name="T"/>.</summary>
    public static T Create<T>(Guid clsid) where T : class
    {
        var iid = typeof(T).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(clsid, 0, CLSCTX_INPROC_SERVER, iid, out var ptr));
        return Wrap<T>(ptr);
    }

    /// <summary>Wraps a raw interface pointer and takes over its reference.</summary>
    public static T Wrap<T>(nint ptr) where T : class
    {
        try
        {
            return (T)Wrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    /// <summary>The raw IUnknown of a wrapped object (AddRef'ed; release it).</summary>
    public static nint Unknown(object obj) =>
        ComWrappers.TryGetComInstance(obj, out var ptr) ? ptr : throw new InvalidOperationException("Not a COM object.");

    /// <summary>Releases the native object now instead of waiting for the GC.</summary>
    public static void Free(object? obj)
    {
        if (obj is ComObject com)
            com.FinalRelease();
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid fmtid, uint pid)
{
    public Guid FormatId = fmtid;
    public uint PropertyId = pid;

    /// <summary>System.Title: the caption of a jump list item.</summary>
    public static readonly PropertyKey Title = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
}

/// <summary>PROPVARIANT, just enough for strings (VT_LPWSTR).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort Type;
    private ushort _reserved1, _reserved2, _reserved3;
    public nint Value;
    private nint _padding;

    public const ushort VT_LPWSTR = 31;

    public static PropVariant FromString(string value) => new() { Type = VT_LPWSTR, Value = Marshal.StringToCoTaskMemUni(value) };

    public void Clear()
    {
        if (Type == VT_LPWSTR && Value != 0)
            Marshal.FreeCoTaskMem(Value);
        this = default;
    }
}

[GeneratedComInterface]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal partial interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PropertyKey key);
    [PreserveSig] int GetValue(in PropertyKey key, out PropVariant value);
    [PreserveSig] int SetValue(in PropertyKey key, in PropVariant value);
    [PreserveSig] int Commit();
}

[GeneratedComInterface]
[Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
internal partial interface IObjectArray
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, in Guid riid, out nint ppv);
}

[GeneratedComInterface]
[Guid("5632B1A4-E38A-400A-928A-D4CD63230295")]
internal partial interface IObjectCollection : IObjectArray
{
    [PreserveSig] int AddObject(nint unknown);
    [PreserveSig] int AddFromArray(IObjectArray source);
    [PreserveSig] int RemoveObjectAt(uint index);
    [PreserveSig] int Clear();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal unsafe partial interface IShellLinkW
{
    [PreserveSig] int GetPath(char* file, int cch, nint findData, uint flags);
    [PreserveSig] int GetIDList(out nint pidl);
    [PreserveSig] int SetIDList(nint pidl);
    [PreserveSig] int GetDescription(char* name, int cch);
    [PreserveSig] int SetDescription(string name);
    [PreserveSig] int GetWorkingDirectory(char* dir, int cch);
    [PreserveSig] int SetWorkingDirectory(string dir);
    [PreserveSig] int GetArguments(char* args, int cch);
    [PreserveSig] int SetArguments(string args);
    [PreserveSig] int GetHotkey(out ushort hotkey);
    [PreserveSig] int SetHotkey(ushort hotkey);
    [PreserveSig] int GetShowCmd(out int showCmd);
    [PreserveSig] int SetShowCmd(int showCmd);
    [PreserveSig] int GetIconLocation(char* path, int cch, out int index);
    [PreserveSig] int SetIconLocation(string path, int index);
    [PreserveSig] int SetRelativePath(string path, uint reserved);
    [PreserveSig] int Resolve(nint hwnd, uint flags);
    [PreserveSig] int SetPath(string file);
}

internal static class ShellClsid
{
    public static readonly Guid ShellLink = new("00021401-0000-0000-C000-000000000046");
    public static readonly Guid DestinationList = new("77F10CF0-3DB5-4966-B520-B7C54FD35ED6");
    public static readonly Guid EnumerableObjectCollection = new("2D3468C1-36A7-43B6-AC24-D3F02FD9607A");
}
