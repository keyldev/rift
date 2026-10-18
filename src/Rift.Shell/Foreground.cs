using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Rift.Shell;

[SupportedOSPlatform("windows")]
public static partial class Foreground
{
    private const uint ASFW_ANY = unchecked((uint)-1);

    /// <summary>
    /// Lets the running Rift bring its window to the front. Windows only allows that to the process
    /// the user just interacted with, which is this short-lived second instance.
    /// </summary>
    public static void AllowAny() => AllowSetForegroundWindow(ASFW_ANY);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);
}
