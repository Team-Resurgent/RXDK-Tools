#include "xbdm.h"

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <xboxdbg.h>
#include <ixbconn.h>

#include <cstring>
#include <new>

extern "C" void XboxdbgStaticInit(void);

struct XbdmConnection
{
    IXboxConnection *conn;
};

static thread_local int g_lastHr = 0;
static thread_local char g_lastError[512];

static void SetLastErrorHr(HRESULT hr)
{
    g_lastHr = (int)hr;
    if (FAILED(hr))
        DmTranslateErrorA(hr, g_lastError, (int)sizeof(g_lastError));
    else
        g_lastError[0] = '\0';
}

static int HrToApi(HRESULT hr)
{
    SetLastErrorHr(hr);
    return FAILED(hr) ? -1 : 0;
}

static int64_t FileTimeToUnix(const FILETIME &ft);
static void FillDirEntry(XbdmDirEntry *entry, const DM_FILE_ATTRIBUTES &fa);

static int64_t FileTimeToUnix(const FILETIME &ft)
{
    ULARGE_INTEGER uli;
    uli.LowPart = ft.dwLowDateTime;
    uli.HighPart = ft.dwHighDateTime;
    if (uli.QuadPart == 0)
        return 0;
    return (int64_t)((uli.QuadPart - 116444736000000000ULL) / 10000000ULL);
}

static void FillDirEntry(XbdmDirEntry *entry, const DM_FILE_ATTRIBUTES &fa)
{
    strncpy_s(entry->name, fa.Name, _TRUNCATE);
    entry->size = ((uint64_t)fa.SizeHigh << 32) | fa.SizeLow;
    entry->attributes = fa.Attributes;
    entry->change_time_unix = FileTimeToUnix(fa.ChangeTime);
}

static HRESULT ConnectInternal(const char *consoleName, IXboxConnection **ppConn)
{
    HRESULT hr;

    if (!consoleName || !consoleName[0] || !ppConn)
        return E_INVALIDARG;

    *ppConn = nullptr;
    hr = DmGetXboxConnection(consoleName, XBCONN_VERSION, ppConn);
    if (FAILED(hr))
        return hr;

    hr = (*ppConn)->HrSetConnectionTimeout(10000, 30000);
    if (FAILED(hr))
    {
        (*ppConn)->Release();
        *ppConn = nullptr;
    }
    return hr;
}

extern "C" int xbdm_init(XbdmAbiInfo *info)
{
    XboxdbgStaticInit();
    if (info)
    {
        info->abi_version = XBDM_ABI_VERSION;
        info->build = 1;
    }
    SetLastErrorHr(S_OK);
    return 0;
}

extern "C" void xbdm_shutdown(void)
{
}

extern "C" int xbdm_get_default_console_name(char *buf, int buf_len)
{
    DWORD cch;

    if (!buf || buf_len <= 0)
        return HrToApi(E_INVALIDARG);

    cch = (DWORD)buf_len;
    return HrToApi(DmGetXboxName(buf, &cch));
}

extern "C" int xbdm_set_default_console_name(const char *name)
{
    if (!name || !name[0])
        return HrToApi(E_INVALIDARG);
    return HrToApi(DmSetXboxName(name));
}

extern "C" int xbdm_connect(const char *console_name, XbdmConnection **out)
{
    HRESULT hr;
    XbdmConnection *handle;

    if (!out)
        return HrToApi(E_INVALIDARG);

    *out = nullptr;
    handle = new (std::nothrow) XbdmConnection();
    if (!handle)
        return HrToApi(E_OUTOFMEMORY);

    handle->conn = nullptr;
    hr = ConnectInternal(console_name, &handle->conn);
    if (FAILED(hr))
    {
        delete handle;
        return HrToApi(hr);
    }

    *out = handle;
    return HrToApi(S_OK);
}

extern "C" void xbdm_disconnect(XbdmConnection *conn)
{
    if (!conn)
        return;
    if (conn->conn)
        conn->conn->Release();
    delete conn;
}

extern "C" int xbdm_list_drives(XbdmConnection *conn, char *drives, int *drive_count)
{
    HRESULT hr;
    DWORD cDrives;

    if (!conn || !conn->conn || !drives || !drive_count || *drive_count <= 0)
        return HrToApi(E_INVALIDARG);

    cDrives = (DWORD)*drive_count;
    hr = conn->conn->HrGetDriveList(drives, &cDrives);
    if (FAILED(hr))
        return HrToApi(hr);

    *drive_count = (int)cDrives;
    return HrToApi(S_OK);
}

extern "C" int xbdm_list_dir(
    XbdmConnection *conn,
    const char *wire_path,
    XbdmDirEntry *entries,
    int max_entries,
    int *entry_count)
{
    HRESULT hr;
    PDM_WALK_DIR walkDir = nullptr;
    int count = 0;

    if (!conn || !conn->conn || !wire_path || !entries || max_entries <= 0 || !entry_count)
        return HrToApi(E_INVALIDARG);

    *entry_count = 0;
    hr = conn->conn->HrOpenDir(&walkDir, wire_path, nullptr);
    if (FAILED(hr))
        return HrToApi(hr);

    for (;;)
    {
        DM_FILE_ATTRIBUTES fa;

        hr = conn->conn->HrWalkDir(&walkDir, wire_path, &fa);
        if (FAILED(hr))
            break;

        if (count < max_entries)
        {
            XbdmDirEntry *entry = &entries[count];
            FillDirEntry(entry, fa);
            ++count;
        }
    }

    conn->conn->HrCloseDir(walkDir);

    if (count == 0 && FAILED(hr))
        return HrToApi(hr);

    *entry_count = count;
    return HrToApi(S_OK);
}

extern "C" int xbdm_get_file_attributes(XbdmConnection *conn, const char *wire_path, XbdmDirEntry *out)
{
    HRESULT hr;
    DM_FILE_ATTRIBUTES fa;

    if (!conn || !conn->conn || !wire_path || !out)
        return HrToApi(E_INVALIDARG);

    hr = conn->conn->HrGetFileAttributes(wire_path, &fa);
    if (FAILED(hr))
        return HrToApi(hr);

    FillDirEntry(out, fa);
    return HrToApi(S_OK);
}

extern "C" int xbdm_send_file(XbdmConnection *conn, const char *local_path, const char *wire_path)
{
    if (!conn || !conn->conn || !local_path || !wire_path)
        return HrToApi(E_INVALIDARG);
    return HrToApi(conn->conn->HrSendFile(local_path, wire_path));
}

extern "C" int xbdm_receive_file(XbdmConnection *conn, const char *wire_path, const char *local_path)
{
    if (!conn || !conn->conn || !wire_path || !local_path)
        return HrToApi(E_INVALIDARG);
    return HrToApi(conn->conn->HrReceiveFile(local_path, wire_path));
}

extern "C" int xbdm_delete(XbdmConnection *conn, const char *wire_path, int is_directory)
{
    if (!conn || !conn->conn || !wire_path)
        return HrToApi(E_INVALIDARG);
    return HrToApi(conn->conn->HrDeleteFile(wire_path, is_directory ? TRUE : FALSE));
}

extern "C" int xbdm_rename(XbdmConnection *conn, const char *from_wire, const char *to_wire)
{
    if (!conn || !conn->conn || !from_wire || !to_wire)
        return HrToApi(E_INVALIDARG);
    return HrToApi(conn->conn->HrRenameFile(from_wire, to_wire));
}

extern "C" int xbdm_create_directory(XbdmConnection *conn, const char *wire_path)
{
    if (!conn || !conn->conn || !wire_path)
        return HrToApi(E_INVALIDARG);
    return HrToApi(conn->conn->HrMkdir(wire_path));
}

extern "C" int xbdm_reboot(XbdmConnection *conn, int cold, const char *launch_path)
{
    DWORD flags = cold ? 0 : DMBOOT_WARM;
    if (!conn || !conn->conn)
        return HrToApi(E_INVALIDARG);
    return HrToApi(conn->conn->HrReboot(flags, launch_path));
}

static int AddressToApi(HRESULT hr, uint32_t *out, DWORD value)
{
    if (!out)
        return HrToApi(E_INVALIDARG);
    if (FAILED(hr))
        return HrToApi(hr);
    *out = value;
    return HrToApi(S_OK);
}

extern "C" int xbdm_get_disk_free_space(XbdmConnection *conn, const char *drive_wire, uint64_t *free_bytes, uint64_t *total_bytes)
{
    HRESULT hr;
    ULARGE_INTEGER freeSpace = {0};
    ULARGE_INTEGER totalSpace = {0};
    ULARGE_INTEGER bogus = {0};

    if (!conn || !conn->conn || !drive_wire || !free_bytes || !total_bytes)
        return HrToApi(E_INVALIDARG);

    hr = conn->conn->HrGetDiskFreeSpace(const_cast<LPSTR>(drive_wire), &freeSpace, &totalSpace, &bogus);
    if (FAILED(hr))
        return HrToApi(hr);

    *free_bytes = freeSpace.QuadPart;
    *total_bytes = totalSpace.QuadPart;
    return HrToApi(S_OK);
}

extern "C" int xbdm_resolve_xbox_address(XbdmConnection *conn, uint32_t *address_out)
{
    DWORD address = 0;
    if (!conn || !conn->conn)
        return HrToApi(E_INVALIDARG);
    return AddressToApi(conn->conn->HrResolveXboxName(&address), address_out, address);
}

extern "C" int xbdm_get_alt_address(XbdmConnection *conn, uint32_t *address_out)
{
    DWORD address = 0;
    if (!conn || !conn->conn)
        return HrToApi(E_INVALIDARG);
    return AddressToApi(conn->conn->HrGetAltAddress(&address), address_out, address);
}

extern "C" int xbdm_get_name_of_xbox(XbdmConnection *conn, char *buf, int buf_len, int resolvable)
{
    DWORD cch;
    if (!conn || !conn->conn || !buf || buf_len <= 0)
        return HrToApi(E_INVALIDARG);
    cch = (DWORD)buf_len;
    return HrToApi(conn->conn->HrGetNameOfXbox(buf, &cch, resolvable ? TRUE : FALSE));
}

extern "C" int xbdm_get_xbe_launch_path(XbdmConnection *conn, char *buf, int buf_len)
{
    HRESULT hr;
    DM_XBE xbe;

    if (!conn || !conn->conn || !buf || buf_len <= 0)
        return HrToApi(E_INVALIDARG);

    hr = conn->conn->HrGetXbeInfo(NULL, &xbe);
    if (FAILED(hr))
        return HrToApi(hr);

    strncpy_s(buf, (size_t)buf_len, xbe.LaunchPath, _TRUNCATE);
    return HrToApi(S_OK);
}

extern "C" int xbdm_screenshot(XbdmConnection *conn, const char *local_bmp_path)
{
    if (!conn || !conn->conn || !local_bmp_path)
        return HrToApi(E_INVALIDARG);
    return HrToApi(conn->conn->HrScreenShot(local_bmp_path));
}

extern "C" int xbdm_set_file_attributes(XbdmConnection *conn, const char *wire_path, uint32_t attributes)
{
    HRESULT hr;
    DM_FILE_ATTRIBUTES fa;

    if (!conn || !conn->conn || !wire_path)
        return HrToApi(E_INVALIDARG);

    hr = conn->conn->HrGetFileAttributes(wire_path, &fa);
    if (FAILED(hr))
        return HrToApi(hr);

    fa.Attributes = attributes;
    return HrToApi(conn->conn->HrSetFileAttributes(wire_path, &fa));
}

extern "C" int xbdm_is_security_enabled(XbdmConnection *conn, int *enabled)
{
    HRESULT hr;
    BOOL locked = FALSE;

    if (!conn || !conn->conn || !enabled)
        return HrToApi(E_INVALIDARG);

    hr = conn->conn->HrIsSecurityEnabled(&locked);
    if (FAILED(hr))
        return HrToApi(hr);

    *enabled = locked ? 1 : 0;
    return HrToApi(S_OK);
}

extern "C" int xbdm_security_supports_user_priv(XbdmConnection *conn, int *supported)
{
    HRESULT hr;
    char buffer[255];
    DWORD size = sizeof(buffer);

    if (!conn || !conn->conn || !supported)
        return HrToApi(E_INVALIDARG);

    hr = conn->conn->HrSendCommand("GETUSERPRIV NAME=BOGUS", buffer, &size);
    *supported = (hr == XBDM_INVALIDCMD) ? 1 : 0;
    return HrToApi(S_OK);
}

extern "C" int xbdm_get_user_access(XbdmConnection *conn, const char *user_name, uint32_t *access)
{
    HRESULT hr;
    DWORD dwAccess = 0;

    if (!conn || !conn->conn || !access)
        return HrToApi(E_INVALIDARG);

    hr = conn->conn->HrGetUserAccess(user_name, &dwAccess);
    if (hr == XBDM_UNDEFINED && (!user_name || !user_name[0]))
    {
        char computerName[MAX_COMPUTERNAME_LENGTH + 1];
        DWORD cch = sizeof(computerName);
        if (GetComputerNameA(computerName, &cch))
            hr = conn->conn->HrGetUserAccess(computerName, &dwAccess);
    }

    if (FAILED(hr))
        return HrToApi(hr);

    *access = dwAccess;
    return HrToApi(S_OK);
}

extern "C" int xbdm_list_users(XbdmConnection *conn, XbdmUser *users, int max_users, int *user_count)
{
    HRESULT hr;
    PDM_WALK_USERS walk = NULL;
    DWORD count = 0;
    int written = 0;

    if (!conn || !conn->conn || !users || max_users <= 0 || !user_count)
        return HrToApi(E_INVALIDARG);

    *user_count = 0;
    hr = conn->conn->HrOpenUserList(&walk, &count);
    if (FAILED(hr))
        return HrToApi(hr);

    while (count-- > 0 && written < max_users)
    {
        DM_USER dmUser;
        hr = conn->conn->HrWalkUserList(&walk, &dmUser);
        if (FAILED(hr))
            break;

        strncpy_s(users[written].user_name, dmUser.UserName, _TRUNCATE);
        users[written].access_privileges = dmUser.AccessPrivileges;
        ++written;
    }

    conn->conn->HrCloseUserList(walk);
    if (written == 0 && FAILED(hr))
        return HrToApi(hr);

    *user_count = written;
    return HrToApi(S_OK);
}

extern "C" int xbdm_add_user(XbdmConnection *conn, const char *user_name, uint32_t access)
{
    if (!conn || !conn->conn || !user_name)
        return HrToApi(E_INVALIDARG);
    return HrToApi(conn->conn->HrAddUser(user_name, access));
}

extern "C" int xbdm_remove_user(XbdmConnection *conn, const char *user_name)
{
    if (!conn || !conn->conn || !user_name)
        return HrToApi(E_INVALIDARG);
    return HrToApi(conn->conn->HrRemoveUser(user_name));
}

extern "C" int xbdm_set_user_access(XbdmConnection *conn, const char *user_name, uint32_t access)
{
    if (!conn || !conn->conn || !user_name)
        return HrToApi(E_INVALIDARG);
    return HrToApi(conn->conn->HrSetUserAccess(user_name, access));
}

extern "C" int xbdm_enable_security(XbdmConnection *conn, int enable)
{
    if (!conn || !conn->conn)
        return HrToApi(E_INVALIDARG);
    return HrToApi(conn->conn->HrEnableSecurity(enable ? TRUE : FALSE));
}

extern "C" int xbdm_set_admin_password(XbdmConnection *conn, const char *password)
{
    if (!conn || !conn->conn || !password)
        return HrToApi(E_INVALIDARG);
    return HrToApi(conn->conn->HrSetAdminPassword(password));
}

extern "C" int xbdm_use_secure_connection(XbdmConnection *conn, const char *password)
{
    if (!conn || !conn->conn || !password)
        return HrToApi(E_INVALIDARG);
    return HrToApi(conn->conn->HrUseSecureConnection(password));
}

extern "C" int xbdm_use_shared_connection(XbdmConnection *conn, int enable)
{
    if (!conn || !conn->conn)
        return HrToApi(E_INVALIDARG);
    return HrToApi(conn->conn->HrUseSharedConnection(enable ? TRUE : FALSE));
}

extern "C" int xbdm_last_hresult(void)
{
    return g_lastHr;
}

extern "C" int xbdm_last_error_message(char *buf, int buf_len)
{
    if (!buf || buf_len <= 0)
        return -1;

    strncpy_s(buf, (size_t)buf_len, g_lastError, _TRUNCATE);
    return 0;
}
