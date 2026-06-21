using System.Runtime.InteropServices;

namespace Rxdk.XbShellExt.Ui;

internal static class ShellDialogDpi
{
    public const float DesignDpi = 96f;

    public static Size ScaleDesignSize(Control control, Size size)
    {
        var scale = control.DeviceDpi / DesignDpi;
        return new Size(
            (int)Math.Round(size.Width * scale),
            (int)Math.Round(size.Height * scale));
    }

    public static void ReleaseFixedSizeLocks(Form form)
    {
        form.MinimumSize = Size.Empty;
        form.MaximumSize = Size.Empty;
    }

    /// <summary>
    /// Scale a shell dialog from its 96 DPI design size to the owner monitor DPI.
    /// WinForms AutoScaleMode.Dpi often reports the correct DeviceDpi in Explorer hosts
    /// without resizing layout, so always scale from the 96 DPI baseline.
    /// </summary>
    public static bool ApplyShellDpiScale(Form form, nint ownerHwnd = 0, Size? designClientSize = null)
    {
        ownerHwnd = ResolveOwnerHwnd(form, ownerHwnd);
        var targetDpi = ResolveDpi(ownerHwnd);
        if (targetDpi <= DesignDpi + 1)
            return false;

        var factor = targetDpi / DesignDpi;

        ReleaseFixedSizeLocks(form);
        form.AutoScaleMode = AutoScaleMode.None;
        DisableAutoScale(form);

        form.SuspendLayout();
        form.Scale(new SizeF(factor, factor));
        if (designClientSize is { } designSize)
        {
            form.ClientSize = new Size(
                (int)Math.Round(designSize.Width * factor),
                (int)Math.Round(designSize.Height * factor));
        }

        form.ResumeLayout(true);
        form.PerformLayout();
        return true;
    }

    private static void DisableAutoScale(Control control)
    {
        switch (control)
        {
        case Form form:
            form.AutoScaleMode = AutoScaleMode.None;
            break;
        case UserControl userControl:
            userControl.AutoScaleMode = AutoScaleMode.None;
            break;
        case ContainerControl containerControl:
            containerControl.AutoScaleMode = AutoScaleMode.None;
            break;
        }

        foreach (Control child in control.Controls)
            DisableAutoScale(child);
    }

    public static int ResolveDpi(nint ownerHwnd)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            if (ownerHwnd != 0)
            {
                var ownerDpi = GetDpiForWindow(ownerHwnd);
                if (ownerDpi > 0)
                    return ownerDpi;

                var monitor = MonitorFromWindow(ownerHwnd, MonitorDefaultToNearest);
                if (monitor != 0 &&
                    GetDpiForMonitor(monitor, MonitorDpiType.EffectiveDpi, out var dpiX, out _) == 0 &&
                    dpiX > 0)
                {
                    return (int)dpiX;
                }
            }

            var systemDpi = GetDpiForSystem();
            if (systemDpi > 0)
                return systemDpi;
        }

        return (int)DesignDpi;
    }

    private static nint ResolveOwnerHwnd(Form form, nint ownerHwnd)
    {
        if (ownerHwnd != 0)
            return GetRootHwnd(ownerHwnd);

        if (form.Owner?.Handle is nint ownerFormHwnd && ownerFormHwnd != 0)
            return GetRootHwnd(ownerFormHwnd);

        return 0;
    }

    private static nint GetRootHwnd(nint hwnd)
    {
        var root = GetAncestor(hwnd, GetAncestorRoot);
        return root != 0 ? root : hwnd;
    }

    private const uint MonitorDefaultToNearest = 2;
    private const uint GetAncestorRoot = 2;

    private enum MonitorDpiType
    {
        EffectiveDpi = 0,
    }

    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);
}
