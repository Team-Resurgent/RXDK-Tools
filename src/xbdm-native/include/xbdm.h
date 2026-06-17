#ifndef XBDM_H
#define XBDM_H

#include <stdint.h>

#ifdef _WIN32
#ifdef XBDM_EXPORTS
#define XBDM_API __declspec(dllexport)
#else
#define XBDM_API __declspec(dllimport)
#endif
#else
#define XBDM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define XBDM_ABI_VERSION 1
#define XBDM_MAX_NAME 256
#define XBDM_MAX_PATH 260
#define XBDM_MAX_DRIVES 32

#define XBDM_ATTR_DIRECTORY 0x10

typedef struct XbdmConnection XbdmConnection;

typedef struct XbdmAbiInfo
{
    uint32_t abi_version;
    uint32_t build;
} XbdmAbiInfo;

typedef struct XbdmDirEntry
{
    char name[XBDM_MAX_NAME];
    uint64_t size;
    uint32_t attributes;
    int64_t change_time_unix;
} XbdmDirEntry;

typedef struct XbdmUser
{
    char user_name[XBDM_MAX_NAME];
    uint32_t access_privileges;
} XbdmUser;

#define XBDM_PRIV_READ 0x0001
#define XBDM_PRIV_WRITE 0x0002
#define XBDM_PRIV_CONTROL 0x0004
#define XBDM_PRIV_CONFIGURE 0x0008
#define XBDM_PRIV_MANAGE 0x0010
#define XBDM_PRIV_ALL 0x001F
#define XBDM_PRIV_INITIAL 0x0003

#define XBDM_ATTR_READONLY 0x01
#define XBDM_ATTR_HIDDEN 0x02

XBDM_API int xbdm_init(XbdmAbiInfo *info);
XBDM_API void xbdm_shutdown(void);

XBDM_API int xbdm_get_default_console_name(char *buf, int buf_len);
XBDM_API int xbdm_set_default_console_name(const char *name);

XBDM_API int xbdm_connect(const char *console_name, XbdmConnection **out);
XBDM_API void xbdm_disconnect(XbdmConnection *conn);

XBDM_API int xbdm_list_drives(XbdmConnection *conn, char *drives, int *drive_count);
XBDM_API int xbdm_list_dir(
    XbdmConnection *conn,
    const char *wire_path,
    XbdmDirEntry *entries,
    int max_entries,
    int *entry_count);

XBDM_API int xbdm_get_file_attributes(XbdmConnection *conn, const char *wire_path, XbdmDirEntry *out);

XBDM_API int xbdm_send_file(XbdmConnection *conn, const char *local_path, const char *wire_path);
XBDM_API int xbdm_receive_file(XbdmConnection *conn, const char *wire_path, const char *local_path);
XBDM_API int xbdm_delete(XbdmConnection *conn, const char *wire_path, int is_directory);
XBDM_API int xbdm_rename(XbdmConnection *conn, const char *from_wire, const char *to_wire);
XBDM_API int xbdm_create_directory(XbdmConnection *conn, const char *wire_path);

XBDM_API int xbdm_reboot(XbdmConnection *conn, int cold, const char *launch_path);

XBDM_API int xbdm_get_disk_free_space(XbdmConnection *conn, const char *drive_wire, uint64_t *free_bytes, uint64_t *total_bytes);
XBDM_API int xbdm_resolve_xbox_address(XbdmConnection *conn, uint32_t *address_out);
XBDM_API int xbdm_get_alt_address(XbdmConnection *conn, uint32_t *address_out);
XBDM_API int xbdm_get_name_of_xbox(XbdmConnection *conn, char *buf, int buf_len, int resolvable);
XBDM_API int xbdm_get_xbe_launch_path(XbdmConnection *conn, char *buf, int buf_len);
XBDM_API int xbdm_screenshot(XbdmConnection *conn, const char *local_bmp_path);
XBDM_API int xbdm_set_file_attributes(XbdmConnection *conn, const char *wire_path, uint32_t attributes);

XBDM_API int xbdm_is_security_enabled(XbdmConnection *conn, int *enabled);
XBDM_API int xbdm_security_supports_user_priv(XbdmConnection *conn, int *supported);
XBDM_API int xbdm_get_user_access(XbdmConnection *conn, const char *user_name, uint32_t *access);
XBDM_API int xbdm_list_users(XbdmConnection *conn, XbdmUser *users, int max_users, int *user_count);
XBDM_API int xbdm_add_user(XbdmConnection *conn, const char *user_name, uint32_t access);
XBDM_API int xbdm_remove_user(XbdmConnection *conn, const char *user_name);
XBDM_API int xbdm_set_user_access(XbdmConnection *conn, const char *user_name, uint32_t access);
XBDM_API int xbdm_enable_security(XbdmConnection *conn, int enable);
XBDM_API int xbdm_set_admin_password(XbdmConnection *conn, const char *password);
XBDM_API int xbdm_use_secure_connection(XbdmConnection *conn, const char *password);
XBDM_API int xbdm_use_shared_connection(XbdmConnection *conn, int enable);

XBDM_API int xbdm_last_hresult(void);
XBDM_API int xbdm_last_error_message(char *buf, int buf_len);

#ifdef __cplusplus
}
#endif

#endif
