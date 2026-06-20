using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Rxdk.XbShellExt.Ui;

internal static class ShellModernChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    public static void Apply(Form form)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return;

        void OnHandleCreated(object? _, EventArgs __) => ApplyChrome(form.Handle);

        if (form.IsHandleCreated)
            ApplyChrome(form.Handle);
        else
            form.HandleCreated += OnHandleCreated;
    }

    private static void ApplyChrome(nint hwnd)
    {
        if (hwnd == 0)
            return;

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var round = DwmwcpRound;
            _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            var dark = AppsUseDarkTheme() ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref dark, sizeof(int));
        }
    }

    private static bool AppsUseDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
