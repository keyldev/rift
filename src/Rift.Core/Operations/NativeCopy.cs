using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Rift.Core.Operations;

/// <summary>
/// File copy through <c>CopyFile2</c>: the kernel does the copy (server-side for SMB, block cloning on ReFS),
/// keeps timestamps, attributes and alternate streams, and calls back after every chunk for progress,
/// pause and cancel.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class NativeCopy
{
    private const uint CopyFileFailIfExists = 0x1;

    private const int CallbackChunkFinished = 2;
    private const int ProgressContinue = 0;
    private const int ProgressCancel = 1;

    private const int ErrorRequestAborted = 1235;
    internal const int ErrorFileExists = unchecked((int)0x80070050);
    internal const int ErrorAlreadyExists = unchecked((int)0x800700B7);

    // No COPY_FILE_NO_BUFFERING for big files: on NVMe here 2 GB took 7 s unbuffered against 0.5 s through the cache.

    /// <summary>Called with the bytes copied since the last call; returns false to cancel.</summary>
    internal delegate bool ChunkCallback(long bytes);

    /// <summary>Copies one file. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>
    /// like <see cref="File.Copy(string, string, bool)"/>, and <see cref="OperationCanceledException"/> when the
    /// callback cancels (the destination is removed by the system then).</summary>
    public static void Copy(string source, string target, bool overwrite, ChunkCallback onChunk)
    {
        var state = new State(onChunk);
        var handle = GCHandle.Alloc(state);
        try
        {
            var parameters = new ExtendedParameters
            {
                Size = (uint)sizeof(ExtendedParameters),
                CopyFlags = overwrite ? 0 : CopyFileFailIfExists,
                ProgressRoutine = &OnProgress,
                CallbackContext = (void*)GCHandle.ToIntPtr(handle),
            };

            var hr = CopyFile2(source, target, &parameters);
            if (hr >= 0)
                return;
            if (state.Canceled || hr == HResultFromWin32(ErrorRequestAborted))
                throw new OperationCanceledException();
            throw ToException(hr, target);
        }
        finally
        {
            handle.Free();
        }
    }

    [UnmanagedCallersOnly]
    private static int OnProgress(Message* message, void* context)
    {
        if (message->Type != CallbackChunkFinished)
            return ProgressContinue;

        var state = (State)GCHandle.FromIntPtr((nint)context).Target!;
        var total = (long)message->ChunkFinished.TotalBytesTransferred;
        var delta = total - state.Reported;
        state.Reported = total;
        try
        {
            if (state.OnChunk(delta))
                return ProgressContinue;
        }
        catch
        {
            // Never let an exception cross into native code.
        }
        state.Canceled = true;
        return ProgressCancel;
    }

    private static Exception ToException(int hr, string target)
    {
        var win32 = (hr & 0xFFFF0000) == 0x80070000 ? hr & 0xFFFF : 0;
        var message = win32 != 0 ? new Win32Exception(win32).Message : Marshal.GetExceptionForHR(hr)?.Message ?? $"0x{hr:X8}";
        return win32 is 5 // ERROR_ACCESS_DENIED
            ? new UnauthorizedAccessException(message) { HResult = hr }
            : new IOException(message, hr);
    }

    private static int HResultFromWin32(int error) => unchecked((int)0x80070000 | error);

    /// <summary>.NET adds the <c>\\?\</c> prefix for long paths itself; a direct call has to.</summary>
    internal static string LongPath(string path) =>
        path.Length < 260 || path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
        : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..]
        : @"\\?\" + path;

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CopyFile2(string existingFileName, string newFileName, ExtendedParameters* parameters);

    private sealed class State(ChunkCallback onChunk)
    {
        public readonly ChunkCallback OnChunk = onChunk;
        public long Reported;
        public bool Canceled;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedParameters
    {
        public uint Size;
        public uint CopyFlags;
        public int* Cancel;
        public delegate* unmanaged<Message*, void*, int> ProgressRoutine;
        public void* CallbackContext;
    }

    // COPYFILE2_MESSAGE: a type tag and a union; only ChunkFinished is read.
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public int Type;
        public uint Padding;
        public ChunkFinishedInfo ChunkFinished;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ChunkFinishedInfo
    {
        public uint StreamNumber;
        public uint Flags;
        public nint SourceFile;
        public nint DestinationFile;
        public ulong ChunkNumber;
        public ulong ChunkSize;
        public ulong StreamSize;
        public ulong StreamBytesTransferred;
        public ulong TotalFileSize;
        public ulong TotalBytesTransferred;
    }
}
