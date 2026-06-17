using RXDKNeighborhood.Core.Models;
using Rxdk.Xbdm;
using Rxdk.Xbdm.Managed;

namespace RXDKNeighborhood.Core.Services;

public sealed class SecurityService
{
    public void ApplyChanges(XbdmConnection conn, SecurityEditorState state)
    {
        if (!state.ManageMode)
            return;

        if (!state.Users.Any(u => !u.MarkedForRemove && (u.NewAccess & XbdmConstants.PrivManage) != 0))
            throw new InvalidOperationException("At least one user must retain Manage privileges.");

        conn.UseSharedConnection(true);
        try
        {
            foreach (var user in state.Users)
            {
                if (user.MarkedForRemove)
                {
                    if (!user.IsNew)
                        conn.RemoveUser(user.UserName);
                    continue;
                }

                if (user.IsNew)
                {
                    conn.AddUser(user.UserName, user.NewAccess);
                    user.IsNew = false;
                    user.OriginalAccess = user.NewAccess;
                    continue;
                }

                if (user.OriginalAccess != user.NewAccess)
                {
                    conn.SetUserAccess(user.UserName, user.NewAccess);
                    user.OriginalAccess = user.NewAccess;
                }
            }

            state.Users.RemoveAll(u => u.MarkedForRemove);
            foreach (var user in state.Users)
                user.MarkedForRemove = false;
        }
        finally
        {
            conn.UseSharedConnection(false);
        }
    }

    public void UnlockConsole(XbdmConnection conn, SecurityEditorState state)
    {
        conn.EnableSecurity(false);
        state.IsLocked = false;
        state.ManageMode = false;
        state.Users.Clear();
        state.SelectedUserName = null;
    }

    public void LockConsole(XbdmConnection conn, SecurityEditorState state, string computerName)
    {
        conn.UseSharedConnection(true);
        try
        {
            conn.EnableSecurity(true);
            conn.AddUser(computerName, XbdmConstants.PrivAll);
        }
        finally
        {
            conn.UseSharedConnection(false);
        }

        state.IsLocked = true;
        state.ManageMode = true;
        ReloadUsers(conn, state);
    }

    public void StartSecureMode(XbdmConnection conn, SecurityEditorState state, string password)
    {
        conn.UseSharedConnection(true);
        try
        {
            conn.UseSecureConnection(password);
            state.CurrentAccess = conn.GetUserAccess();
            if ((state.CurrentAccess & XbdmConstants.PrivManage) != 0)
            {
                state.ManageMode = true;
                ReloadUsers(conn, state);
            }
        }
        finally
        {
            conn.UseSharedConnection(false);
        }
    }

    public void SetAdminPassword(XbdmConnection conn, string password) =>
        conn.SetAdminPassword(password);

    public SecurityUserEntry AddUser(SecurityEditorState state, string userName)
    {
        var existing = state.Users.FirstOrDefault(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.MarkedForRemove = false;
            state.SelectedUserName = existing.UserName;
            return existing;
        }

        var entry = new SecurityUserEntry
        {
            UserName = userName,
            IsNew = true,
            NewAccess = XbdmConstants.PrivInitial,
            OriginalAccess = 0,
        };
        state.Users.Add(entry);
        state.SelectedUserName = entry.UserName;
        return entry;
    }

    public void RemoveUser(SecurityEditorState state, string userName)
    {
        var user = state.Users.FirstOrDefault(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
        if (user == null)
            return;

        user.MarkedForRemove = true;
        if (state.SelectedUserName == user.UserName)
            state.SelectedUserName = state.Users.FirstOrDefault(u => !u.MarkedForRemove)?.UserName;
    }

    public static string GetComputerName() => Environment.MachineName;

    private static void ReloadUsers(XbdmConnection conn, SecurityEditorState state)
    {
        state.Users.Clear();
        foreach (var user in conn.ListUsers())
        {
            state.Users.Add(new SecurityUserEntry
            {
                UserName = user.UserName,
                OriginalAccess = user.AccessPrivileges,
                NewAccess = user.AccessPrivileges,
            });
        }

        state.SelectedUserName = state.Users.FirstOrDefault()?.UserName;
    }
}
