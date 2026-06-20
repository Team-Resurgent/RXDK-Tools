using Rxdk.XbShellExt.Ui.Forms;
using Rxdk.Xbdm.KitServices.Services;

namespace Rxdk.XbShellExt.Ui;

public static class ShellUiHost
{
    public static void ShowProperties(nint ownerHwnd, RXDKNeighborhood.Core.Services.PropertyRequest? request, string? initialTab = null)
    {
        if (request == null)
            return;

        RunSta(ownerHwnd, () =>
        {
            using var form = new PropertiesForm(request, initialTab);
            form.ShowDialog(NativeWindow.FromHandle(ownerHwnd));
        });
    }

    public static void ShowError(nint ownerHwnd, string message)
    {
        RunSta(ownerHwnd, () =>
            MessageBox.Show(
                NativeWindow.FromHandle(ownerHwnd),
                message,
                "Xbox Neighborhood",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning));
    }

    public static void RunAddConsoleWizard(nint ownerHwnd)
    {
        RunSta(ownerHwnd, () =>
        {
            using var form = new AddConsoleWizardForm();
            if (form.ShowDialog(NativeWindow.FromHandle(ownerHwnd)) == DialogResult.OK)
            {
                ShellNotify.NotifyFolderTreeChanged();
            }
        });
    }

    private static void RunSta(nint ownerHwnd, Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
        {
            try
            {
                MessageBox.Show(
                    NativeWindow.FromHandle(ownerHwnd),
                    error.Message,
                    "Xbox Neighborhood",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
                throw error;
            }
        }
    }
}

internal static class NativeWindow
{
    public static IWin32Window FromHandle(nint handle) => new Win32Window(handle);

    private sealed class Win32Window : IWin32Window
    {
        public Win32Window(nint handle) => Handle = handle;
        public nint Handle { get; }
    }
}
