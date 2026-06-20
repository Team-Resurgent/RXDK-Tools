using Rxdk.Xbdm.KitServices.Models;
using Rxdk.Xbdm;
using Rxdk.Xbdm.KitServices.Stores;
using Rxdk.Xbdm.Managed;

namespace Rxdk.Xbdm.KitServices.Services;

public class AddConsoleService
{
    public AddConsoleWizardState ProbeConsole(string consoleName)
    {
        var state = new AddConsoleWizardState { ConsoleName = consoleName.Trim() };
        if (string.IsNullOrWhiteSpace(state.ConsoleName))
            return state;

        try
        {
            using var conn = XbdmSession.Connect(state.ConsoleName);
            state.IpAddress = conn.TryResolveXboxAddress();
            if (!state.IpAddress.HasValue)
            {
                state.ConsoleIsValid = false;
                return state;
            }

            state.IsSecurityEnabled = conn.IsSecurityEnabled();
            state.ConsoleIsValid = true;
            try
            {
                state.WireName = conn.GetNameOfXbox(resolvable: false);
            }
            catch
            {
                state.WireName = null;
            }
            if (!state.IsSecurityEnabled)
            {
                state.CurrentAccess = XbdmConstants.PrivAll;
                state.DesiredAccess = XbdmConstants.PrivAll;
                return state;
            }

            try
            {
                state.CurrentAccess = conn.GetUserAccess();
            }
            catch
            {
                state.CurrentAccess = XbdmConstants.PrivAll;
            }

            state.DesiredAccess = state.CurrentAccess;
        }
        catch
        {
            state.ConsoleIsValid = false;
        }

        return state;
    }

    public void ValidatePassword(AddConsoleWizardState state)
    {
        using var conn = XbdmSession.Connect(state.ConsoleName);
        conn.UseSharedConnection(true);
        try
        {
            conn.UseSecureConnection(state.Password);
        }
        finally
        {
            conn.UseSharedConnection(false);
        }
    }

    public void Finish(AddConsoleWizardState state, IConsoleStore consoleStore)
    {
        if (!state.ConsoleIsValid)
            throw new InvalidOperationException("Console has not been validated.");

        if (state.NeedsSecurityStep && state.DesiredAccess != state.CurrentAccess)
        {
            using var conn = XbdmSession.Connect(state.ConsoleName);
            conn.UseSharedConnection(true);
            try
            {
                conn.UseSecureConnection(state.Password);
                var computer = SecurityService.GetComputerName();
                try
                {
                    conn.SetUserAccess(computer, state.DesiredAccess);
                }
                catch (XbdmException)
                {
                    conn.AddUser(computer, state.DesiredAccess);
                }
            }
            finally
            {
                conn.UseSharedConnection(false);
            }
        }

        var registryName = ResolveRegistryName(state);
        var addressStore = new ShellExtensionConsoleAddressStore();
        consoleStore.AddConsole(registryName);

        if (state.IpAddress.HasValue)
        {
            var ipName = FormattingHelper.FormatIpAddress(state.IpAddress.Value);
            addressStore.SetAddress(registryName, ipName);
        }

        if (state.MakeDefault)
            consoleStore.SetDefaultConsole(registryName);
    }

    private static string ResolveRegistryName(AddConsoleWizardState state)
    {
        if (!string.IsNullOrWhiteSpace(state.WireName))
            return state.WireName.Trim();

        return state.ConsoleName.Trim();
    }
}
