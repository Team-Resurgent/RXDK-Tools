using RXDKNeighborhood.Core.Models;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace RXDKNeighborhood.Core.Services;

public sealed class AddConsoleService
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

    public void Finish(AddConsoleWizardState state, ConsoleRegistryService registry)
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

        registry.AddConsole(state.ConsoleName);
        if (state.MakeDefault)
            registry.SetDefaultConsole(state.ConsoleName);
    }
}
