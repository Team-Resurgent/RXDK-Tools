using System.ComponentModel;
using System.Diagnostics;
using Rxdk.Xbdm;
using Rxdk.Xbdm.KitServices.Models;

namespace Rxdk.XbShellExt.Ui;

internal static class DesignPreview
{
    public static bool IsDesignTime(IComponent? component = null)
    {
        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
            return true;

        if (component?.Site?.DesignMode == true)
            return true;

        var processName = Process.GetCurrentProcess().ProcessName;
        return processName.Contains("devenv", StringComparison.OrdinalIgnoreCase)
            || processName.Contains("DesignToolsServer", StringComparison.OrdinalIgnoreCase)
            || processName.Contains("WDExpress", StringComparison.OrdinalIgnoreCase);
    }

    public static void ApplyIfDesignTime(Action action, IComponent? component = null)
    {
        if (IsDesignTime(component))
            action();
    }

    public const string SampleConsoleName = "myxbox";
    public const string SampleConsoleCaption = "myxbox";
    public const string SampleConsoleIp = "192.168.0.100";
    public const string SampleConsoleAltIp = "fe80::1";
    public const string SampleRunningTitle = "default.xbe";

    public static FileGeneralInfo SampleFile => new()
    {
        Items =
        [
            new FileSelectionItem
            {
                Name = "Halo.xbe",
                WirePath = @"myxbox\C\Games\Halo.xbe",
                IsDirectory = false,
            },
        ],
        DisplayName = "Halo.xbe",
        TypeName = "Xbox Executable",
        Location = @"myxbox\C\Games",
        TotalSize = 4_194_304,
        Created = new DateTimeOffset(2004, 11, 9, 14, 30, 0, TimeSpan.Zero),
        Modified = new DateTimeOffset(2005, 3, 15, 9, 12, 0, TimeSpan.Zero),
        ValidAttributes = XbdmConstants.AttrReadOnly | XbdmConstants.AttrHidden,
        ReadOnly = false,
        Hidden = false,
    };

    public static FileGeneralInfo SampleFolder => new()
    {
        Items =
        [
            new FileSelectionItem
            {
                Name = "Games",
                WirePath = @"myxbox\C\Games",
                IsDirectory = true,
            },
        ],
        DisplayName = "Games",
        TypeName = "Folder",
        Location = @"myxbox\C",
        FileCount = 12,
        FolderCount = 3,
        Created = new DateTimeOffset(2004, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Modified = new DateTimeOffset(2005, 6, 1, 18, 45, 0, TimeSpan.Zero),
        ValidAttributes = XbdmConstants.AttrReadOnly | XbdmConstants.AttrHidden,
        ReadOnly = false,
        Hidden = false,
    };

    public static DriveGeneralInfo SampleDrive => new()
    {
        Letter = 'C',
        Description = "Hard Disk (C:)",
        DriveType = "Hard Disk Drive",
        TotalBytes = 8_589_934_592,
        FreeBytes = 3_221_225_472,
    };

    public const string SampleAccessText = "Full access";

    public const string SampleAttributeChangesSummary =
        "Clear Read-only attribute\r\nSet Hidden attribute";

    public static uint SampleDesiredAccess => XbdmConstants.PrivAll;
}
