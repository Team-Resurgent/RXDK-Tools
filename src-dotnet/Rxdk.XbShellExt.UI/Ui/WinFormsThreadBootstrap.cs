using System.Runtime.InteropServices;

namespace Rxdk.XbShellExt.Ui;

public static class WinFormsThreadBootstrap
{
    private static readonly nint PerMonitorV2DpiContext = new(-4);

    private static int _initialized;

    public static void EnsureInitialized()
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
            return;

        // Explorer's UI thread may already have chosen a process-wide DPI mode.
        // Always set this worker STA thread to PerMonitorV2 before creating controls.
        SetThreadDpiAwarenessContext(PerMonitorV2DpiContext);

        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        }
        catch (InvalidOperationException)
        {
            // Process-wide mode was already configured.
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint dpiContext);
}
