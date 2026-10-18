using System.Runtime.Versioning;
using static Rift.Shell.Native;

namespace Rift.Shell;

[SupportedOSPlatform("windows")]
public static class RecycleBin
{
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
