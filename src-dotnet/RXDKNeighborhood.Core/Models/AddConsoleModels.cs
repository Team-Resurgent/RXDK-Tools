using Rxdk.Xbdm;

namespace RXDKNeighborhood.Core.Models;

public sealed class AddConsoleWizardState
{
    public string ConsoleName { get; set; } = "";
    public uint? IpAddress { get; set; }
    public bool ConsoleIsValid { get; set; }
    public bool IsSecurityEnabled { get; set; }
    public uint CurrentAccess { get; set; }
    public uint DesiredAccess { get; set; }
    public string Password { get; set; } = "";
    public bool MakeDefault { get; set; } = true;

    public bool NeedsSecurityStep =>
        IsSecurityEnabled && (CurrentAccess & XbdmConstants.PrivAll) != XbdmConstants.PrivAll;
}
