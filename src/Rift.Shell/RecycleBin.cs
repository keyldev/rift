using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using static Rift.Shell.Native;

namespace Rift.Shell;

[SupportedOSPlatform("windows")]
public static partial class RecycleBin
{
    /// <summary>This user's bin folder on every drive that has one (X:\$Recycle.Bin\&lt;SID&gt;).</summary>
    public static IReadOnlyList<string> Folders()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (sid is null)
            return [];
        var folders = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType is DriveType.Fixed or DriveType.Removable && drive.IsReady)
                {
                    var folder = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", sid);
                    if (Directory.Exists(folder))
                        folders.Add(folder);
                }
            }
            catch (IOException)
            {
                // Drive went away.
            }
        }
        return folders;
    }

    /// <summary>Empties the bin on all drives, with Windows' own progress window. Call from the UI thread.</summary>
    public static void Empty(nint owner)
    {
        SHEmptyRecycleBinW(owner, null, SHERB_NOCONFIRMATION);
        NotifyChanged();
    }

    /// <summary>
    /// Tells Explorer the bin changed behind its back (restore, permanent delete):
    /// open bin windows refresh and the desktop icon switches between full and empty.
    /// </summary>
    public static void NotifyChanged()
    {
        SHUpdateRecycleBinIcon();
        if (SHGetSpecialFolderLocation(0, CSIDL_BITBUCKET, out var pidl) != 0)
            return;
        SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_IDLIST | SHCNF_FLUSHNOWAIT, pidl, 0);
        Marshal.FreeCoTaskMem(pidl);
    }

    private const uint SHERB_NOCONFIRMATION = 0x1;
    private const int SHCNE_UPDATEDIR = 0x1000;
    private const uint SHCNF_IDLIST = 0;
    private const uint SHCNF_FLUSHNOWAIT = 0x3000;
    private const int CSIDL_BITBUCKET = 0x0A;

    [LibraryImport("shell32.dll")]
    private static partial int SHGetSpecialFolderLocation(nint hwnd, int folder, out nint pidl);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBinW(nint hwnd, string? rootPath, uint flags);

    [LibraryImport("shell32.dll")]
    private static partial void SHUpdateRecycleBinIcon();

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);

    /// <summary>
    /// Moves files and folders to the Recycle Bin, so they can be restored from there.
    /// Windows itself warns when something can't be recycled (network drives, too large).
    /// </summary>
    /// <returns>False if the user aborted the operation.</returns>
    public static Task<bool> SendAsync(IReadOnlyList<string> paths, nint owner)
    {
        if (paths.Count == 0)
            return Task.FromResult(true);

        // Shell file operations want an STA thread and may show UI; don't block the UI thread.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(Send(paths, owner));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Rift recycle bin",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static unsafe bool Send(IReadOnlyList<string> paths, nint owner)
    {
        // pFrom is a list of NUL-separated paths ending with a double NUL.
        var from = string.Join('\0', paths) + "\0\0";
        fixed (char* p = from)
        {
            var op = new SHFILEOPSTRUCTW
            {
                hwnd = owner,
                wFunc = FO_DELETE,
                pFrom = (nint)p,
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT | FOF_WANTNUKEWARNING,
            };
            var result = SHFileOperationW(ref op);
            if (result != 0)
                throw new IOException($"Couldn't move to the Recycle Bin (0x{result:X}).");
            return op.fAnyOperationsAborted == 0;
        }
    }
}
