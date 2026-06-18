using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Tests.Parity;

internal static partial class XbdmParityChecks
{
    private const string SecurityCategory = "Security";

    /// <summary>
    /// End-to-end security scenario driven by the managed C# client (our shipping target). Each
    /// step mutates through <see cref="XbdmParitySession.Managed"/>; native xbdm.dll is read back
    /// for parity. Kit ends unlocked for exec/bridge suites.
    /// </summary>
    private static IEnumerable<ParityCheckResult> RunSecurityRoundTrip(XbdmParitySession session)
    {
        const string testUser = "rxdkparity";
        var password = SecurityTestPassword();
        var computerName = Environment.MachineName;
        var results = new List<ParityCheckResult>();

        if (!session.Managed.SupportsUserPrivileges())
        {
            results.Add(ParityCompare.Skip(
                SecurityCategory,
                "SecurityRoundTrip",
                "User privileges not supported on this kit."));
            return results;
        }

        var restoreNeeded = false;
        try
        {
            ParityProgress.Phase("Security: ensuring kit starts unlocked (managed)");
            if (!TryEnsureUnlocked(session, password, results))
                return results;

            ParityProgress.Phase("Security: locking kit via managed (PC user manage — no admin password)");
            LockConsole(session, computerName, password);
            restoreNeeded = true;
            results.Add(CompareSecurityEnabledManaged(session, "LockConsole", expectedLocked: true));

            XbdmParityWait.PauseForUser(
                $"Security: kit locked — {computerName} has all privileges",
                session,
                releaseKitForNeighborhood: true,
                adminPasswordHint: password);

            ParityProgress.Phase($"Security: granting {computerName} reduced privileges (read+write+manage, not all)");
            var defaultAccess = XbdmConstants.PrivRead | XbdmConstants.PrivWrite | XbdmConstants.PrivManage;
            session.Managed.SetUserAccess(computerName, defaultAccess);
            results.Add(CompareUserAccessManaged(session, computerName, defaultAccess, "SetDefaultAccess"));

            ParityProgress.Phase($"Security: adding test user {testUser}");
            TryRemoveUser(session.Managed, testUser);
            session.Managed.AddUser(testUser, XbdmConstants.PrivRead);
            results.Add(CompareUserPresentManaged(session, testUser, "AddUser"));

            ParityProgress.Phase("Security: tightening test user privileges");
            session.Managed.SetUserAccess(testUser, XbdmConstants.PrivInitial);
            results.Add(CompareUserAccessManaged(session, testUser, XbdmConstants.PrivInitial, "SetUserAccess"));
            session.ReconnectNative(password);
            results.Add(CompareListUsersParity(session, SecurityCategory, "ListUsers(locked)"));

            XbdmParityWait.PauseForUser(
                $"Security: verify users — {computerName}=read+write+manage, {testUser}=read+write only",
                session,
                releaseKitForNeighborhood: true,
                adminPasswordHint: password);

            ParityProgress.Phase("Security: restoring kit to unlocked (managed)");
            RestoreUnlocked(session, testUser);
            restoreNeeded = false;
            session.ReleaseKitConnections();
            session.ReconnectKit();
            results.Add(CompareSecurityEnabled(session, "UnlockConsole", expectedLocked: false));
            results.Add(ParityCompare.Pass(SecurityCategory, "SecurityRoundTrip", "managed lock → configure → unlock"));
        }
        catch (Exception ex)
        {
            results.Add(ParityCompare.Fail(SecurityCategory, "SecurityRoundTrip", ex.Message));
        }
        finally
        {
            if (restoreNeeded)
            {
                try
                {
                    ParityProgress.Phase("Security: cleanup after failure — unlocking kit");
                    RestoreUnlocked(session, testUser);
                }
                catch (Exception ex)
                {
                    ParityProgress.Phase($"Security: cleanup failed: {ex.Message}");
                }
            }

            try
            {
                session.ReconnectKit();
            }
            catch (Exception ex)
            {
                ParityProgress.Phase($"Security: reconnect failed: {ex.Message}");
            }
        }

        return results;
    }

    private static bool TryEnsureUnlocked(
        XbdmParitySession session,
        string password,
        List<ParityCheckResult> results)
    {
        if (!session.Managed.IsSecurityEnabled())
        {
            results.Add(ParityCompare.Pass(SecurityCategory, "EnsureUnlocked", "already unlocked"));
            return true;
        }

        try
        {
            session.RunManagedExclusive(() =>
            {
                session.ReconnectManaged();
                session.Managed.EnableSecurity(false);
            });
            session.ReconnectNative();
            results.Add(CompareSecurityEnabled(session, "EnsureUnlocked", expectedLocked: false));
            return results[^1].Status != ParityStatus.Failed;
        }
        catch (Exception ex)
        {
            results.Add(ParityCompare.Fail(
                SecurityCategory,
                "EnsureUnlocked",
                $"Kit is locked; could not unlock with PC user auth: {ex.Message}"));
            return false;
        }
    }

    private static void LockConsole(XbdmParitySession session, string computerName, string adminPassword)
    {
        session.RunManagedExclusive(() =>
        {
            ParityProgress.Phase("Security: sending LOCKMODE (managed)");
            session.Managed.EnableSecurity(true);

            ParityProgress.Phase($"Security: adding manage user {computerName} (managed)");
            try
            {
                session.Managed.AddUser(computerName, XbdmConstants.PrivAll);
            }
            catch (XbdmException ex) when (ex.HResultCode == XbdmHResults.AlreadyExists)
            {
                session.Managed.SetUserAccess(computerName, XbdmConstants.PrivAll);
            }

            ParityProgress.Phase($"Security: setting admin password for Neighborhood Manage… ({adminPassword})");
            session.Managed.SetAdminPassword(adminPassword);
            VerifyAdminPassword(session, adminPassword);
        });
    }

    private static void VerifyAdminPassword(XbdmParitySession session, string adminPassword)
    {
        session.ReconnectManaged(adminPassword);
        if ((session.Managed.GetUserAccess() & XbdmConstants.PrivManage) == 0)
            throw new InvalidOperationException(
                $"Admin password '{adminPassword}' was set but AUTHUSER ADMIN failed on reconnect.");
        ParityProgress.Phase($"Security: admin password '{adminPassword}' verified (AUTHUSER ADMIN)");
    }

    private static void RestoreUnlocked(XbdmParitySession session, string testUser)
    {
        session.RunManagedExclusive(() =>
        {
            TryRemoveUser(session.Managed, testUser);
            session.Managed.EnableSecurity(false);
        });
    }

    private static void TryRemoveUser(IXbdmConnection conn, string userName)
    {
        try
        {
            conn.RemoveUser(userName);
        }
        catch (XbdmException)
        {
        }
    }

    private static ParityCheckResult CompareSecurityEnabledManaged(
        XbdmParitySession session,
        string step,
        bool expectedLocked)
    {
        var managed = session.Managed.IsSecurityEnabled();
        if (managed == expectedLocked)
        {
            return ParityCompare.Pass(
                SecurityCategory,
                step,
                expectedLocked ? "locked (managed session)" : "unlocked");
        }

        return ParityCompare.Fail(
            SecurityCategory,
            step,
            $"Expected locked={expectedLocked}.",
            "(native not connected)",
            managed ? "locked" : "unlocked");
    }

    private static ParityCheckResult CompareSecurityEnabled(
        XbdmParitySession session,
        string step,
        bool expectedLocked)
    {
        var native = session.Native.IsSecurityEnabled();
        var managed = session.Managed.IsSecurityEnabled();
        if (native == expectedLocked && managed == expectedLocked && native == managed)
        {
            return ParityCompare.Pass(
                SecurityCategory,
                step,
                expectedLocked ? "locked" : "unlocked");
        }

        return ParityCompare.Fail(
            SecurityCategory,
            step,
            $"Expected locked={expectedLocked}.",
            native ? "locked" : "unlocked",
            managed ? "locked" : "unlocked");
    }

    private static ParityCheckResult CompareUserPresentManaged(
        XbdmParitySession session,
        string userName,
        string step)
    {
        var managed = session.Managed.ListUsers().Any(u =>
            string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
        return managed
            ? ParityCompare.Pass(SecurityCategory, step, $"{userName} (managed while locked)")
            : ParityCompare.Fail(
                SecurityCategory,
                step,
                "Test user missing after add.",
                "(native not connected)",
                managed ? "present" : "missing");
    }

    private static ParityCheckResult CompareUserAccessManaged(
        XbdmParitySession session,
        string userName,
        uint expectedAccess,
        string step)
    {
        var managed = FindUserAccess(session.Managed, userName);
        if (managed == expectedAccess)
            return ParityCompare.Pass(SecurityCategory, step, $"{userName}=0x{expectedAccess:x} (managed while locked)");

        return ParityCompare.Fail(
            SecurityCategory,
            step,
            $"Access mismatch for {userName}.",
            "(native not connected)",
            $"0x{managed:x}");
    }

    private static uint FindUserAccess(IXbdmConnection conn, string userName)
    {
        var user = conn.ListUsers().FirstOrDefault(u =>
            string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
        return user?.AccessPrivileges ?? 0;
    }
}
