namespace Rxdk.XbNeighborhood.Core.Services;

public static class FormattingHelper
{
    public static string FormatFileSize(ulong size) =>
        Rxdk.Xbdm.KitServices.Services.FormattingHelper.FormatFileSize(size);

    public static string FormatFileSizeBytes(ulong bytes) =>
        Rxdk.Xbdm.KitServices.Services.FormattingHelper.FormatFileSizeBytes(bytes);

    public static string FormatIpAddress(uint address) =>
        Rxdk.Xbdm.KitServices.Services.FormattingHelper.FormatIpAddress(address);

    public static string FormatDateTime(DateTimeOffset? value) =>
        Rxdk.Xbdm.KitServices.Services.FormattingHelper.FormatDateTime(value);

    public static string BuildLocationString(string displayPath, string consoleName) =>
        Rxdk.Xbdm.KitServices.Services.FormattingHelper.BuildLocationString(displayPath, consoleName);

    public static string GetDriveTypeName(char letter) =>
        Rxdk.Xbdm.KitServices.Services.FormattingHelper.GetDriveTypeName(letter);
}
