using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace Rxdk.Xbdm.Tests.Parity;

/// <summary>
/// Recovery helper when a parity security test leaves the kit locked.
/// </summary>
internal static class XbdmKitUnlock
{
    internal static int Run(string console, string? adminPassword)
    {
        XbdmSciRegistry.ReleaseConsole(console);

        using var client = new ManagedXbdmClient();
        client.Initialize();
        try
        {
            using (var probe = (ManagedXbdmConnection)client.Connect(console))
            {
                if (!probe.IsSecurityEnabled())
                {
                    Console.WriteLine($"Kit '{console}' is already unlocked.");
                    return 0;
                }
            }

            Console.WriteLine($"Kit '{console}' is locked — attempting unlock...");
            Console.WriteLine(
                $"PC user auth uses seed at {SecuritySeedHint()} (or HKLM\\Software\\Microsoft\\XboxSDK).");

            var candidates = new List<string?> { null };
            if (!string.IsNullOrWhiteSpace(adminPassword))
                candidates.Add(adminPassword);
            if (!candidates.Contains("test", StringComparer.Ordinal))
                candidates.Add("test");

            foreach (var password in candidates)
            {
                try
                {
                    using var conn = OpenManageSession(client, console, password);
                    conn.EnableSecurity(false);
                    Console.WriteLine(
                        password is null
                            ? "Unlocked using PC user credentials (no admin password)."
                            : $"Unlocked using admin password '{password}'.");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        password is null
                            ? $"PC user unlock failed: {ex.Message}"
                            : $"Admin password '{password}' failed: {ex.Message}");
                }
            }

            Console.Error.WriteLine(
                "Could not unlock the kit. If parity saved a seed under AppData, original Neighborhood " +
                "may need that same machine connection — or reboot the Xbox and retry unlock.");
            return 1;
        }
        finally
        {
            client.Shutdown();
        }
    }

    internal static void TryAuthenticateManage(XbdmParitySession session, string adminPassword)
    {
        if (!session.Managed.IsSecurityEnabled())
            return;

        if (HasManageAccess(session.Managed))
            return;

        session.ReconnectManaged();
        if (HasManageAccess(session.Managed))
            return;

        foreach (var password in new[] { adminPassword, "test" }
                     .Where(p => !string.IsNullOrWhiteSpace(p))
                     .Distinct(StringComparer.Ordinal))
        {
            try
            {
                session.ReconnectManaged(password);
                if (HasManageAccess(session.Managed))
                    return;
            }
            catch (XbdmException)
            {
            }
        }

        throw new InvalidOperationException("Could not obtain manage access on locked kit.");
    }

    private static ManagedXbdmConnection OpenManageSession(
        ManagedXbdmClient client,
        string console,
        string? adminPassword)
    {
        ManagedXbdmConnection conn = adminPassword is null
            ? (ManagedXbdmConnection)client.Connect(console)
            : (ManagedXbdmConnection)client.Connect(
                console,
                new XbdmConnectOptions { AdminPassword = adminPassword });

        if (!HasManageAccess(conn))
            throw new InvalidOperationException("Connected but manage access was denied.");

        return conn;
    }

    private static bool HasManageAccess(IXbdmConnection conn)
    {
        try
        {
            return (conn.GetUserAccess() & XbdmConstants.PrivManage) != 0;
        }
        catch (XbdmException)
        {
            return false;
        }
    }

    private static string SecuritySeedHint()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "RXDKNeighborhood", "xbdm_security_seed.bin");
    }
}
