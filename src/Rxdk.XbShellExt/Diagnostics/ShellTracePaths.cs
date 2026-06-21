namespace Rxdk.XbShellExt.Diagnostics;

internal static class ShellTracePaths
{
    internal const string LogFolderName = "Xbox Neighborhood";
    internal const string LogSubFolder = "Logs";
    internal const string ManagedLogFileName = "xb-shlext-mgd.log";
    internal const string NativeLogFileName = "xb-shlext.log";

    internal static string LogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            LogFolderName,
            LogSubFolder);

    internal static string ManagedLogPath => Path.Combine(LogDirectory, ManagedLogFileName);

    internal static string NativeLogPath => Path.Combine(LogDirectory, NativeLogFileName);
}
