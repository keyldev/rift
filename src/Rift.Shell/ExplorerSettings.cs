using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Rift.Shell;

/// <summary>Explorer's own view preferences, used as Rift's defaults.</summary>
[SupportedOSPlatform("windows")]
public static class ExplorerSettings
{
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    /// <summary>"Show hidden files, folders and drives" in Explorer's options (Hidden = 1).</summary>
    public static bool ShowsHiddenFiles()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey);
        return key?.GetValue("Hidden") is 1;
    }
}
