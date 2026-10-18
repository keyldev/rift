using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Rift.Shell;

/// <summary>
/// "Открыть в Rift" in Explorer's context menu for folders, empty space inside a folder and drives.
/// Written per user (HKCU\Software\Classes), so no admin rights are needed.
/// On Windows 11 classic verbs live under "Show more options"; a top-level entry needs
/// IExplorerCommand + a sparse package (see ROADMAP.md).
/// </summary>
[SupportedOSPlatform("windows")]
public static class ExplorerIntegration
{
    public const string VerbName = "Rift";
    public const string MenuText = "Открыть в Rift";

    private const string ClassesRoot = @"Software\Classes";

    /// <summary>Where the verb is registered and which placeholder carries the folder path.</summary>
    public static readonly IReadOnlyList<(string ShellKey, string Placeholder)> Targets =
    [
        (@"Directory\shell", "%1"),
        (@"Directory\Background\shell", "%V"),
        (@"Drive\shell", "%1"),
    ];

    public static string Command(string exePath, string placeholder) => $"\"{exePath}\" \"{placeholder}\"";

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(VerbKey(Targets[0].ShellKey) + @"\command");
        return key?.GetValue(null) is string;
    }

    public static void Register(string exePath)
    {
        foreach (var (shellKey, placeholder) in Targets)
        {
            using var verb = Registry.CurrentUser.CreateSubKey(VerbKey(shellKey));
            verb.SetValue(null, MenuText);
            verb.SetValue("Icon", $"\"{exePath}\",0");
            using var command = verb.CreateSubKey("command");
            command.SetValue(null, Command(exePath, placeholder));
        }
    }

    public static void Unregister()
    {
        foreach (var (shellKey, _) in Targets)
            Registry.CurrentUser.DeleteSubKeyTree(VerbKey(shellKey), throwOnMissingSubKey: false);
    }

    private static string VerbKey(string shellKey) => $@"{ClassesRoot}\{shellKey}\{VerbName}";
}
