using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace Rift.Shell;

/// <summary>
/// Explorer's own right-click menu (IContextMenu), with everything shell extensions add to it:
/// 7-Zip, Git, "Send to", "Open with", antivirus scans… Shown on the UI thread, it blocks until closed.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class ShellContextMenu
{
    /// <summary>
    /// Lets Rift run some verbs itself (rename inline, delete with undo, open folders in a tab).
    /// Returns true if Rift handled the verb; otherwise the shell runs it.
    /// </summary>
    public delegate bool VerbInterceptor(string verb);

    /// <summary>The menu for selected files and folders (all in one folder).</summary>
    /// <param name="extended">Shift was held: show extended verbs, like Explorer.</param>
    public static void ShowForItems(nint owner, IReadOnlyList<string> paths, int screenX, int screenY,
        bool extended, VerbInterceptor intercept)
    {
        using var items = ShellItems.FromPaths(paths);
        if (items is null)
            return;
        var menu = Com.Wrap<IContextMenu>(items.GetUIObject(owner, typeof(IContextMenu).GUID));
        Show(owner, menu, Path.GetDirectoryName(paths[0]), screenX, screenY, extended, intercept);
    }

    /// <summary>The menu for empty space inside a folder: New, Paste, Properties…</summary>
    public static void ShowForFolder(nint owner, string folder, int screenX, int screenY, bool extended, VerbInterceptor intercept)
    {
        var shellFolder = ShellItems.FolderFromPath(folder);
        try
        {
            Marshal.ThrowExceptionForHR(shellFolder.CreateViewObject(owner, typeof(IContextMenu).GUID, out var ptr));
            Show(owner, Com.Wrap<IContextMenu>(ptr), folder, screenX, screenY, extended, intercept);
        }
        finally
        {
            Com.Free(shellFolder);
        }
    }

    private const uint FirstCommand = 1;
    private const uint LastCommand = 0x7FFF;

    private static void Show(nint owner, IContextMenu menu, string? directory, int x, int y, bool extended, VerbInterceptor intercept)
    {
        var hmenu = CreatePopupMenu();
        nint window = 0;
        try
        {
            var flags = CMF_NORMAL | CMF_CANRENAME | (extended ? CMF_EXTENDEDVERBS : 0);
            Marshal.ThrowExceptionForHR(menu.QueryContextMenu(hmenu, 0, FirstCommand, LastCommand, flags));

            // Submenus like "Send to" and "Open with" fill themselves through window messages.
            _active = menu as IContextMenu3;
            _active2 = _active is null ? menu as IContextMenu2 : null;
            window = HelperWindow(owner);

            // Without these two the menu doesn't close when clicking elsewhere (documented TrackPopupMenu quirk).
            SetForegroundWindow(window);
            var command = TrackPopupMenuEx(hmenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, window, 0);
            PostMessageW(window, WM_NULL, 0, 0);
            if (command == 0)
                return;

            var offset = (uint)command - FirstCommand;
            if (Verb(menu, offset) is { } verb && intercept(verb))
                return;
            Invoke(menu, offset, owner, directory, x, y);
        }
        finally
        {
            _active = null;
            _active2 = null;
            if (window != 0)
                DestroyWindow(window);
            DestroyMenu(hmenu);
            Com.Free(menu);
        }
    }

    private static string? Verb(IContextMenu menu, uint offset)
    {
        var buffer = stackalloc char[256];
        buffer[0] = '\0';
        try
        {
            if (menu.GetCommandString(offset, GCS_VERBW, 0, (byte*)buffer, 256) != Com.S_OK)
                return null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return null;
        }
        var verb = new string(buffer);
        return verb.Length > 0 ? verb.ToLowerInvariant() : null;
    }

    private static void Invoke(IContextMenu menu, uint offset, nint owner, string? directory, int x, int y)
    {
        var dir = directory is null ? 0 : Marshal.StringToHGlobalUni(directory);
        try
        {
            var info = new CMINVOKECOMMANDINFOEX
            {
                cbSize = (uint)sizeof(CMINVOKECOMMANDINFOEX),
                fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                hwnd = owner,
                lpVerb = (nint)offset,
                lpVerbW = (nint)offset,
                lpDirectoryW = dir,
                nShow = SW_SHOWNORMAL,
                ptInvoke = new POINT { X = x, Y = y },
            };
            menu.InvokeCommand(&info);
        }
        finally
        {
            if (dir != 0)
                Marshal.FreeHGlobal(dir);
        }
    }

    // ---------- helper window that owns the menu and forwards its messages ----------

    private static IContextMenu3? _active;
    private static IContextMenu2? _active2;
    private static bool _classRegistered;
    private const string ClassName = "RiftShellMenuOwner";

    private static nint HelperWindow(nint owner)
    {
        var instance = GetModuleHandleW(null);
        if (!_classRegistered)
        {
            fixed (char* name = ClassName)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WndProc,
                    hInstance = instance,
                    lpszClassName = (nint)name,
                };
                RegisterClassExW(&wc);
            }
            _classRegistered = true;
        }
        return CreateWindowExW(0, ClassName, "", WS_POPUP, 0, 0, 0, 0, owner, 0, instance, 0);
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg is WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR)
        {
            try
            {
                if (_active is { } menu3)
                {
                    nint result;
                    if (menu3.HandleMenuMsg2(msg, wParam, lParam, &result) == Com.S_OK)
                        return result;
                }
                else if (_active2 is { } menu2 && menu2.HandleMenuMsg(msg, wParam, lParam) == Com.S_OK)
                {
                    return 0;
                }
            }
            catch (Exception)
            {
                // A broken shell extension must not take Rift down with it.
            }
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // ---------- interop ----------

    private const uint CMF_NORMAL = 0;
    private const uint CMF_EXTENDEDVERBS = 0x100;
    private const uint CMF_CANRENAME = 0x10;
    private const uint GCS_VERBW = 4;
    private const uint CMIC_MASK_UNICODE = 0x4000;
    private const uint CMIC_MASK_PTINVOKE = 0x20000000;
    private const int SW_SHOWNORMAL = 1;
    private const uint TPM_RETURNCMD = 0x100;
    private const uint TPM_RIGHTBUTTON = 0x2;
    private const uint WM_NULL = 0;
    private const uint WM_DRAWITEM = 0x2B;
    private const uint WM_MEASUREITEM = 0x2C;
    private const uint WM_INITMENUPOPUP = 0x117;
    private const uint WM_MENUCHAR = 0x120;
    private const uint WS_POPUP = 0x80000000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CMINVOKECOMMANDINFOEX
    {
        public uint cbSize;
        public uint fMask;
        public nint hwnd;
        public nint lpVerb;
        public nint lpParameters;
        public nint lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public nint hIcon;
        public nint lpTitle;
        public nint lpVerbW;
        public nint lpParametersW;
        public nint lpDirectoryW;
        public nint lpTitleW;
        public POINT ptInvoke;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public nint lpszMenuName;
        public nint lpszClassName;
        public nint hIconSm;
    }

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(WNDCLASSEXW* wc);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}

[GeneratedComInterface]
[Guid("000214E4-0000-0000-C000-000000000046")]
internal unsafe partial interface IContextMenu
{
    [PreserveSig] int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
    [PreserveSig] int InvokeCommand(ShellContextMenu.CMINVOKECOMMANDINFOEX* info);
    [PreserveSig] int GetCommandString(nuint idCmd, uint type, nint reserved, byte* name, uint cchMax);
}

[GeneratedComInterface]
[Guid("000214F4-0000-0000-C000-000000000046")]
internal unsafe partial interface IContextMenu2 : IContextMenu
{
    [PreserveSig] int HandleMenuMsg(uint msg, nint wParam, nint lParam);
}

[GeneratedComInterface]
[Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
internal unsafe partial interface IContextMenu3 : IContextMenu2
{
    [PreserveSig] int HandleMenuMsg2(uint msg, nint wParam, nint lParam, nint* result);
}
