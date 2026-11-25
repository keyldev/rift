using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Rift.Shell;

/// <summary>
/// Keeps DWM non-client frame composition and dark/light border attributes active on Windows 10+.
/// Avalonia 12 disables <c>DWMWA_NCRENDERING_POLICY</c> on Windows 10 when <c>ExtendClientAreaToDecorationsHint</c>
/// is set; with <c>Win32CompositionMode.RedirectionSurface</c> that exposes the unpainted GDI redirection bitmap
/// as white borders around the non-client resize frame.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class WindowFrame
{
    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmncrpEnabled = 2;

    public static void Apply(nint hwnd, bool dark)
    {
        if (hwnd == 0)
            return;

        var policy = DwmncrpEnabled;
        _ = DwmSetWindowAttribute(hwnd, DwmwaNcRenderingPolicy, &policy, sizeof(int));

        var useDark = dark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, &useDark, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, &useDark, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int dwAttribute, void* pvAttribute, int cbAttribute);
}
