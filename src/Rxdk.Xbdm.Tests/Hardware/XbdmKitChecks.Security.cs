using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Tests.Hardware;

internal static partial class XbdmKitChecks
{
    private const string SecurityCategory = "Security";

    /// <summary>
    /// End-to-end security scenario through the managed client. Kit ends unlocked for exec/bridge suites.
    /// </summary>
    private static IEnumerable<KitCheckResult> RunSecurityRoundTrip(XbdmKitSession session)
    {
        const string testUser = "rxdk-kittest";
        var password = SecurityTestPassword();
        var computerName = Environment.MachineName;
        var results = new List<KitCheckResult>();

        if (!session.Managed.SupportsUserPrivileges())
        {
            results.Add(KitCheck.Skip(
                SecurityCategory,
                "SecurityRoundTrip",
                "User privileges not supported on this kit."));
            return results;
        }

        var restoreNeeded = false;
        try
        {
            KitTestProgress.Phase("Security: ensuring kit starts unlocked (managed)");
            if (!TryEnsureUnlocked(session, password, results))
                return results;

            KitTestProgress.Phase("Security: locking kit via managed (PC user manage — no admin password)");
            LockConsole(session, computerName, password);
            restoreNeeded = true;
            results.Add(CompareSecurityEnabledManaged(session, "LockConsole", expectedLocked: true));

            XbdmKitWait.PauseForUser(
                $"Security: kit locked — {computerName} has all privileges",
                session,
                releaseKitForNeighborhood: true,
                adminPasswordHint: password);

            KitTestProgress.Phase($"Security: granting {computerName} reduced privileges (read+write+manage, not all)");
            var defaultAccess = XbdmConstants.PrivRead | XbdmConstants.PrivWrite | XbdmConstants.PrivManage;
            session.Managed.SetUserAccess(computerName, defaultAccess);
            results.Add(CompareUserAccessManaged(session, computerName, defaultAccess, "SetDefaultAccess"));

            KitTestProgress.Phase($"Security: adding test user {testUser}");
            TryRemoveUser(session.Managed, testUser);
            session.Managed.AddUser(testUser, XbdmConstants.PrivRead);
            results.Add(CompareUserPresentManaged(session, testUser, "AddUser"));

            KitTestProgress.Phase("Security: tightening test user privileges");
            session.Managed.SetUserAccess(testUser, XbdmConstants.PrivInitial);
            results.Add(CompareUserAccessManaged(session, testUser, XbdmConstants.PrivInitial, "SetUserAccess"));
            results.Add(CompareListUsersLocked(session, testUser, computerName, defaultAccess));

            XbdmKitWait.PauseForUser(
                $"Security: verify users — {computerName}=read+write+manage, {testUser}=read+write only",
                session,
                releaseKitForNeighborhood: true,
                adminPasswordHint: password);

            KitTestProgress.Phase("Security: restoring kit to unlocked (managed)");
            RestoreUnlocked(session, testUser);
            restoreNeeded = false;
            session.ReleaseKitConnections();
            session.ReconnectKit();
            results.Add(CompareSecurityEnabledManaged(session, "UnlockConsole", expectedLocked: false));
            results.Add(KitCheck.Pass(SecurityCategory, "SecurityRoundTrip", "managed lock → configure → unlock"));
        }
        catch (Exception ex)
        {
            results.Add(KitCheck.Fail(SecurityCategory, "SecurityRoundTrip", ex.Message));
        }
        finally
        {
            if (restoreNeeded)
            {
                try
                {
                    KitTestProgress.Phase("Security: cleanup after failure — unlocking kit");
                    RestoreUnlocked(session, testUser);
                }
                catch (Exception ex)
                {
                    KitTestProgress.Phase($"Security: cleanup failed: {ex.Message}");
                }
            }

            try
            {
                session.ReconnectKit();
            }
            catch (Exception ex)
            {
                KitTestProgress.Phase($"Security: reconnect failed: {ex.Message}");
            }
        }

        return results;
    }

    private static KitCheckResult CompareListUsersLocked(
        XbdmKitSession session,
        string testUser,
        string computerName,
        uint computerAccess)
    {
        if (!session.Managed.IsSecurityEnabled())
            return KitCheck.Skip(SecurityCategory, "ListUsers(locked)", "Kit is unlocked.");

        var users = session.Managed.ListUsers().OrderBy(u => u.UserName).ToArray();
        var test = users.FirstOrDefault(u =>
            string.Equals(u.UserName, testUser, StringComparison.OrdinalIgnoreCase));
        var computer = users.FirstOrDefault(u =>
            string.Equals(u.UserName, computerName, StringComparison.OrdinalIgnoreCase));

        if (test is null)
            return KitCheck.Fail(SecurityCategory, "ListUsers(locked)", $"Test user {testUser} missing.");
        if (computer is null)
            return KitCheck.Fail(SecurityCategory, "ListUsers(locked)", $"Computer user {computerName} missing.");
        if (test.AccessPrivileges != XbdmConstants.PrivInitial)
            return KitCheck.Fail(SecurityCategory, "ListUsers(locked)", $"Unexpected access for {testUser}.", $"0x{test.AccessPrivileges:x}");
        if (computer.AccessPrivileges != computerAccess)
            return KitCheck.Fail(SecurityCategory, "ListUsers(locked)", $"Unexpected access for {computerName}.", $"0x{computer.AccessPrivileges:x}");

        return KitCheck.Pass(SecurityCategory, "ListUsers(locked)", $"{users.Length} users");
    }

    private static bool TryEnsureUnlocked(
        XbdmKitSession session,
        string password,
        List<KitCheckResult> results)
    {
        if (!session.Managed.IsSecurityEnabled())
        {
            results.Add(KitCheck.Pass(SecurityCategory, "EnsureUnlocked", "already unlocked"));
            return true;
        }

        try
        {
            session.RunManagedExclusive(() =>
            {
                session.ReconnectManaged();
                session.Managed.EnableSecurity(false);
            });
            results.Add(CompareSecurityEnabledManaged(session, "EnsureUnlocked", expectedLocked: false));
            return results[^1].Status != KitCheckStatus.Failed;
        }
        catch (Exception ex)
        {
            results.Add(KitCheck.Fail(
                SecurityCategory,
                "EnsureUnlocked",
                $"Kit is locked; could not unlock with PC user auth: {ex.Message}"));
            return false;
        }
    }

    private static void LockConsole(XbdmKitSession session, string computerName, string adminPassword)
    {
        session.RunManagedExclusive(() =>
        {
            KitTestProgress.Phase("Security: sending LOCKMODE (managed)");
            session.Managed.EnableSecurity(true);

            KitTestProgress.Phase($"Security: adding manage user {computerName} (managed)");
            try
            {
                session.Managed.AddUser(computerName, XbdmConstants.PrivAll);
            }
            catch (XbdmException ex) when (ex.HResultCode == XbdmHResults.AlreadyExists)
            {
                session.Managed.SetUserAccess(computerName, XbdmConstants.PrivAll);
            }

            KitTestProgress.Phase($"Security: setting admin password for Neighborhood Manage… ({adminPassword})");
            session.Managed.SetAdminPassword(adminPassword);
            VerifyAdminPassword(session, adminPassword);
        });
    }

    private static void VerifyAdminPassword(XbdmKitSession session, string adminPassword)
    {
        session.ReconnectManaged(adminPassword);
        if ((session.Managed.GetUserAccess() & XbdmConstants.PrivManage) == 0)
            throw new InvalidOperationException(
                $"Admin password '{adminPassword}' was set but AUTHUSER ADMIN failed on reconnect.");
        KitTestProgress.Phase($"Security: admin password '{adminPassword}' verified (AUTHUSER ADMIN)");
    }

    private static void RestoreUnlocked(XbdmKitSession session, string testUser)
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

    private static KitCheckResult CompareSecurityEnabledManaged(
        XbdmKitSession session,
        string step,
        bool expectedLocked)
    {
        var locked = session.Managed.IsSecurityEnabled();
        return locked == expectedLocked
            ? KitCheck.Pass(
                SecurityCategory,
                step,
                expectedLocked ? "locked" : "unlocked")
            : KitCheck.Fail(
                SecurityCategory,
                step,
                $"Expected locked={expectedLocked}.",
                locked ? "locked" : "unlocked");
    }

    private static KitCheckResult CompareUserPresentManaged(
        XbdmKitSession session,
        string userName,
        string step)
    {
        var present = session.Managed.ListUsers().Any(u =>
            string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
        return present
            ? KitCheck.Pass(SecurityCategory, step, $"{userName} present")
            : KitCheck.Fail(SecurityCategory, step, "Test user missing after add.");
    }

    private static KitCheckResult CompareUserAccessManaged(
        XbdmKitSession session,
        string userName,
        uint expectedAccess,
        string step)
    {
        var access = FindUserAccess(session.Managed, userName);
        return access == expectedAccess
            ? KitCheck.Pass(SecurityCategory, step, $"{userName}=0x{expectedAccess:x}")
            : KitCheck.Fail(SecurityCategory, step, $"Access mismatch for {userName}.", $"0x{access:x}");
    }

    private static uint FindUserAccess(IXbdmConnection conn, string userName)
    {
        var user = conn.ListUsers().FirstOrDefault(u =>
            string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
        return user?.AccessPrivileges ?? 0;
    }
}
