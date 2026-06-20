#include "stdafx.h"
#include "NativeFolderOps.h"

#include <algorithm>
#include <new>
#include <vector>

namespace
{
    class PidlCopy
    {
    public:
        static HRESULT copy(LPITEMIDLIST* dest, LPITEMIDLIST* src) throw()
        {
            *dest = CloneShellPidl(reinterpret_cast<PCUIDLIST_RELATIVE>(*src));
            return *dest ? S_OK : E_OUTOFMEMORY;
        }

        static void init(LPITEMIDLIST* dest) throw()
        {
            *dest = nullptr;
        }

        static void destroy(LPITEMIDLIST* dest) throw()
        {
            CoTaskMemFree(*dest);
            *dest = nullptr;
        }
    };

    using CNativeEnumIdList = CComEnum<IEnumIDList, &IID_IEnumIDList, LPITEMIDLIST, PidlCopy, CComMultiThreadModel>;

    bool MatchesEnumFlags(ULONG attributes, DWORD grfFlags)
    {
        const bool foldersOnly = (grfFlags & SHCONTF_FOLDERS) != 0;
        const bool nonFoldersOnly = (grfFlags & SHCONTF_NONFOLDERS) != 0;
        const bool isFolder = (attributes & NativeFolderOps::kSfgaoFolder) != 0;

        if (foldersOnly && !nonFoldersOnly && !isFolder)
            return false;
        if (nonFoldersOnly && !foldersOnly && isFolder)
            return false;
        return true;
    }

    HRESULT SetShellDetailsString(SHELLDETAILS* psd, LPCWSTR text)
    {
        psd->fmt = LVCFMT_LEFT;
        psd->cxChar = 30;
        psd->str.uType = STRRET_WSTR;
        const size_t chars = wcslen(text) + 1;
        auto* buffer = static_cast<WCHAR*>(CoTaskMemAlloc(chars * sizeof(WCHAR)));
        if (!buffer)
            return E_OUTOFMEMORY;
        StringCchCopyW(buffer, chars, text);
        psd->str.pOleStr = buffer;
        return S_OK;
    }

    std::wstring SegmentToDisplayName(LPCSTR segment)
    {
        if (!segment || segment[0] == '\0')
            return L"";

        if (segment[0] == '?')
            segment++;

        const int wideLen = MultiByteToWideChar(CP_ACP, 0, segment, -1, nullptr, 0);
        if (wideLen <= 0)
            return L"";

        std::wstring wide(static_cast<size_t>(wideLen - 1), L'\0');
        MultiByteToWideChar(CP_ACP, 0, segment, -1, wide.data(), wideLen);
        return wide;
    }

    void ReadConsoleNames(std::vector<std::string>& names)
    {
        HKEY key = nullptr;
        if (RegOpenKeyExW(
                HKEY_CURRENT_USER,
                L"Software\\Microsoft\\XboxSDK\\xbshlext\\Consoles",
                0,
                KEY_READ,
                &key) != ERROR_SUCCESS)
        {
            return;
        }

        DWORD index = 0;
        for (;;)
        {
            char name[256] = {};
            DWORD nameLen = static_cast<DWORD>(sizeof(name));
            DWORD type = 0;
            const LONG result = RegEnumValueA(key, index++, name, &nameLen, nullptr, &type, nullptr, nullptr);
            if (result == ERROR_NO_MORE_ITEMS)
                break;
            if (result != ERROR_SUCCESS || name[0] == '\0')
                continue;
            names.emplace_back(name);
        }

        RegCloseKey(key);
        std::sort(names.begin(), names.end(), [](const std::string& a, const std::string& b) {
            return _stricmp(a.c_str(), b.c_str()) < 0;
        });
    }
}

namespace NativeFolderOps
{
    bool ConsoleNameExists(LPCSTR segment);

    LPITEMIDLIST CreateSimplePidl(LPCSTR segment)
    {
        if (!segment)
            return nullptr;

        const size_t segmentLen = strlen(segment) + 1;
        const USHORT cb = static_cast<USHORT>(sizeof(USHORT) + segmentLen);
        const size_t total = cb + sizeof(USHORT);
        auto* pidl = static_cast<LPITEMIDLIST>(CoTaskMemAlloc(total));
        if (!pidl)
            return nullptr;

        pidl->mkid.cb = cb;
        memcpy(pidl->mkid.abID, segment, segmentLen);
        reinterpret_cast<LPITEMIDLIST>(reinterpret_cast<LPBYTE>(pidl) + cb)->mkid.cb = 0;
        return pidl;
    }

    std::string GetLastSegment(LPCITEMIDLIST pidl)
    {
        std::string last;
        if (!pidl)
            return last;

        auto* walk = const_cast<LPITEMIDLIST>(pidl);
        while (walk->mkid.cb)
        {
            last.assign(reinterpret_cast<LPCSTR>(walk->mkid.abID));
            walk = reinterpret_cast<LPITEMIDLIST>(reinterpret_cast<LPBYTE>(walk) + walk->mkid.cb);
        }

        return last;
    }

    UINT CountPidlSegments(LPCITEMIDLIST pidl)
    {
        UINT count = 0;
        if (!pidl)
            return count;

        auto* walk = const_cast<LPITEMIDLIST>(pidl);
        while (walk->mkid.cb)
        {
            ++count;
            walk = reinterpret_cast<LPITEMIDLIST>(reinterpret_cast<LPBYTE>(walk) + walk->mkid.cb);
        }

        return count;
    }

    HRESULT CompareSimplePidls(LPCITEMIDLIST pidl1, LPCITEMIDLIST pidl2)
    {
        auto* left = const_cast<LPITEMIDLIST>(pidl1);
        auto* right = const_cast<LPITEMIDLIST>(pidl2);

        while (left->mkid.cb && right->mkid.cb)
        {
            const int cmp = _stricmp(
                reinterpret_cast<LPCSTR>(left->mkid.abID),
                reinterpret_cast<LPCSTR>(right->mkid.abID));
            if (cmp != 0)
                return cmp < 0 ? static_cast<HRESULT>(0x0000FFFF) : static_cast<HRESULT>(1);

            left = reinterpret_cast<LPITEMIDLIST>(reinterpret_cast<LPBYTE>(left) + left->mkid.cb);
            right = reinterpret_cast<LPITEMIDLIST>(reinterpret_cast<LPBYTE>(right) + right->mkid.cb);
        }

        if (left->mkid.cb)
            return static_cast<HRESULT>(1);
        if (right->mkid.cb)
            return static_cast<HRESULT>(0x0000FFFF);
        return S_OK;
    }

    std::string GetNamespaceRelativePath(LPCITEMIDLIST pidl)
    {
        std::vector<std::string> segments;
        if (!pidl)
            return {};

        auto* walk = const_cast<LPITEMIDLIST>(pidl);
        while (walk->mkid.cb)
        {
            const auto* bytes = reinterpret_cast<const unsigned char*>(walk->mkid.abID);
            const USHORT cbData = static_cast<USHORT>(walk->mkid.cb - sizeof(USHORT));
            bool ascii = cbData > 0;
            size_t len = 0;
            for (USHORT i = 0; i < cbData && ascii; ++i)
            {
                const unsigned char b = bytes[i];
                if (b == 0)
                    break;
                if (b < 0x20 || b > 0x7E)
                {
                    ascii = false;
                    break;
                }
                ++len;
            }

            if (!ascii || len == 0)
                segments.clear();
            else
                segments.emplace_back(reinterpret_cast<LPCSTR>(bytes), len);

            walk = reinterpret_cast<LPITEMIDLIST>(reinterpret_cast<LPBYTE>(walk) + walk->mkid.cb);
        }

        std::string path;
        for (size_t i = 0; i < segments.size(); ++i)
        {
            if (i > 0)
                path += '\\';
            path += segments[i];
        }
        return path;
    }

    ULONG GetChildAttributes(LPCSTR segment)
    {
        if (!segment || segment[0] == '\0')
            return 0;
        if (strcmp(segment, kAddConsoleSegment) == 0)
            return kAddConsoleAttributes;
        if (ConsoleNameExists(segment))
            return kConsoleAttributes;
        if (segment[1] == '\0' && isalpha(static_cast<unsigned char>(segment[0])))
            return kVolumeAttributes;
        return kFileAttributes;
    }

    HRESULT SetStrRetWide(STRRET* pName, const std::wstring& wide)
    {
        pName->uType = STRRET_WSTR;
        const size_t chars = wide.size() + 1;
        auto* buffer = static_cast<WCHAR*>(CoTaskMemAlloc(chars * sizeof(WCHAR)));
        if (!buffer)
            return E_OUTOFMEMORY;
        StringCchCopyW(buffer, chars, wide.c_str());
        pName->pOleStr = buffer;
        return S_OK;
    }

    HRESULT GetShortDisplayName(LPCITEMIDLIST pidl, DWORD uFlags, STRRET* pName)
    {
        const auto segment = GetLastSegment(pidl);
        if ((uFlags & SHGDN_FORPARSING) != 0 &&
            segment.size() == 1 &&
            isalpha(static_cast<unsigned char>(segment[0])))
        {
            wchar_t name[4] = {};
            swprintf_s(name, L"%hc:", static_cast<char>(toupper(static_cast<unsigned char>(segment[0]))));
            return SetStrRetWide(pName, name);
        }

        return SetStrRetWide(pName, SegmentToDisplayName(segment.c_str()));
    }

    std::wstring AnsiPathToWide(const std::string& path)
    {
        if (path.empty())
            return {};

        const int wideLen = MultiByteToWideChar(CP_ACP, 0, path.c_str(), -1, nullptr, 0);
        if (wideLen <= 0)
            return {};

        std::wstring wide(static_cast<size_t>(wideLen - 1), L'\0');
        MultiByteToWideChar(CP_ACP, 0, path.c_str(), -1, wide.data(), wideLen);
        return wide;
    }

    HRESULT GetDisplayName(LPCITEMIDLIST pidl, DWORD uFlags, STRRET* pName)
    {
        if (!pName)
            return E_POINTER;

        if ((uFlags & SHGDN_INFOLDER) != 0 ||
            ((uFlags & SHGDN_FORADDRESSBAR) != 0 && (uFlags & SHGDN_FORPARSING) == 0))
        {
            return GetShortDisplayName(pidl, uFlags, pName);
        }

        if (uFlags == SHGDN_NORMAL)
            return GetShortDisplayName(pidl, uFlags, pName);

        const auto relativePath = GetNamespaceRelativePath(pidl);
        std::wstring full = L"Xbox Neighborhood";
        if (!relativePath.empty())
        {
            full += L'\\';
            full += AnsiPathToWide(relativePath);
        }

        return SetStrRetWide(pName, full);
    }

    HRESULT GetDetailsOf(LPCITEMIDLIST pidl, UINT iColumn, SHELLDETAILS* psd)
    {
        if (!psd || iColumn != 0)
            return E_INVALIDARG;

        if (!pidl)
            return SetShellDetailsString(psd, L"Name");

        const auto wide = SegmentToDisplayName(GetLastSegment(pidl).c_str());
        return SetShellDetailsString(psd, wide.c_str());
    }

    HRESULT EnumRootObjects(DWORD grfFlags, std::vector<LPITEMIDLIST>& pidls)
    {
        if (MatchesEnumFlags(kAddConsoleAttributes | kSfgaoFolder, grfFlags))
        {
            LPITEMIDLIST pidl = CreateSimplePidl(kAddConsoleSegment);
            if (!pidl)
                return E_OUTOFMEMORY;
            pidls.push_back(pidl);
        }

        std::vector<std::string> consoleNames;
        ReadConsoleNames(consoleNames);
        for (const auto& name : consoleNames)
        {
            if (!MatchesEnumFlags(kConsoleAttributes, grfFlags))
                continue;

            LPITEMIDLIST pidl = CreateSimplePidl(name.c_str());
            if (!pidl)
                return E_OUTOFMEMORY;
            pidls.push_back(pidl);
        }

        return S_OK;
    }

    bool ConsoleNameExists(LPCSTR segment)
    {
        if (!segment || segment[0] == '\0')
            return false;
        if (_stricmp(segment, kAddConsoleSegment) == 0)
            return true;

        std::vector<std::string> consoleNames;
        ReadConsoleNames(consoleNames);
        for (const auto& name : consoleNames)
        {
            if (_stricmp(name.c_str(), segment) == 0)
                return true;
        }

        return false;
    }

    bool IsPlausibleChildBind(LPCITEMIDLIST folderPidl, LPCITEMIDLIST childPidl)
    {
        if (!childPidl || !childPidl->mkid.cb)
            return false;

        // BindToObject only accepts single-level relative child PIDLs.
        auto* walk = const_cast<LPITEMIDLIST>(childPidl);
        walk = reinterpret_cast<LPITEMIDLIST>(reinterpret_cast<LPBYTE>(walk) + walk->mkid.cb);
        if (walk->mkid.cb != 0)
            return false;

        const auto segment = GetLastSegment(childPidl);
        if (segment.empty())
            return false;

        const auto nsPath = GetNamespaceRelativePath(folderPidl);
        if (nsPath.empty())
        {
            return ConsoleNameExists(segment.c_str()) ||
                _stricmp(segment.c_str(), kAddConsoleSegment) == 0;
        }

        const auto firstSlash = nsPath.find('\\');
        if (firstSlash == std::string::npos)
        {
            // Single-segment paths such as "myxbox" are a console; its children are
            // drive letters (single alphabetic segments).
            return segment.size() == 1 && isalpha(static_cast<unsigned char>(segment[0]));
        }

        // Two-or-more segments (myxbox\C, myxbox\C\Folder...) are a drive or folder.
        // Children are arbitrary files/folders, but never consoles or Add Xbox.
        if (ConsoleNameExists(segment.c_str()) ||
            _stricmp(segment.c_str(), kAddConsoleSegment) == 0)
        {
            return false;
        }

        return true;
    }

    bool ShouldBindWithoutManagedInner(LPCITEMIDLIST folderPidl, LPCITEMIDLIST childPidl)
    {
        if (!IsPlausibleChildBind(folderPidl, childPidl))
            return false;

        const auto nsPath = GetNamespaceRelativePath(folderPidl);
        if (nsPath.empty())
            return true;

        const auto firstSlash = nsPath.find('\\');
        if (firstSlash == std::string::npos)
        {
            const auto segment = GetLastSegment(childPidl);
            return segment.size() == 1 && isalpha(static_cast<unsigned char>(segment[0]));
        }

        return false;
    }

    std::string WideToAnsiSegment(LPCWSTR wide)
    {
        if (!wide || wide[0] == L'\0')
            return {};

        const int len = WideCharToMultiByte(CP_ACP, 0, wide, -1, nullptr, 0, nullptr, nullptr);
        if (len <= 0)
            return {};

        std::string segment(static_cast<size_t>(len - 1), '\0');
        WideCharToMultiByte(CP_ACP, 0, wide, -1, segment.data(), len, nullptr, nullptr);
        return segment;
    }
}

namespace NativeFolderOps
{
    LPITEMIDLIST CombinePidls(LPCITEMIDLIST parent, LPCITEMIDLIST relativeChild)
    {
        if (!parent || !relativeChild)
            return nullptr;
        return CombineShellPidl(
            reinterpret_cast<PCUIDLIST_RELATIVE>(parent),
            reinterpret_cast<PCUIDLIST_RELATIVE>(relativeChild));
    }

    HRESULT ParseRootDisplayName(
        LPCWSTR displayName,
        ULONG* pchEaten,
        LPITEMIDLIST* ppidl,
        ULONG* pdwAttributes)
    {
        if (!displayName || !ppidl)
            return E_POINTER;

        *ppidl = nullptr;
        if (pchEaten)
            *pchEaten = 0;

        LPCWSTR cursor = displayName;
        while (*cursor == L'\\' || *cursor == L'/')
            ++cursor;

        LPCWSTR end = cursor;
        while (*end && *end != L'\\' && *end != L'/')
            ++end;

        if (end == cursor)
            return E_INVALIDARG;

        std::wstring component(cursor, end - cursor);
        const auto segment = WideToAnsiSegment(component.c_str());
        if (!ConsoleNameExists(segment.c_str()))
            return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);

        LPITEMIDLIST pidl = CreateSimplePidl(segment.c_str());
        if (!pidl)
            return E_OUTOFMEMORY;

        *ppidl = pidl;
        if (pchEaten)
            *pchEaten = static_cast<ULONG>(end - displayName);

        if (pdwAttributes)
            *pdwAttributes &= GetChildAttributes(segment.c_str());

        return S_OK;
    }

    HRESULT GetRootChildCount(DWORD grfFlags, UINT* count)
    {
        if (!count)
            return E_POINTER;

        *count = 0;
        if (MatchesEnumFlags(kAddConsoleAttributes | kSfgaoFolder, grfFlags))
            ++*count;

        std::vector<std::string> consoleNames;
        ReadConsoleNames(consoleNames);
        for (const auto& name : consoleNames)
        {
            if (MatchesEnumFlags(kConsoleAttributes, grfFlags))
                ++*count;
        }

        return S_OK;
    }

    HRESULT CreateNativeEnumIdList(std::vector<LPITEMIDLIST>& pidls, IEnumIDList** ppenumIDList)
    {
        if (!ppenumIDList)
            return E_POINTER;

        *ppenumIDList = nullptr;
        CComObject<CNativeEnumIdList>* enumerator = nullptr;
        RH(CComObject<CNativeEnumIdList>::CreateInstance(&enumerator));

        // AtlFlagTakeOwnership makes CComEnum delete[] this array and
        // CoTaskMemFree each element on release, so it must be its own
        // new[]-allocated buffer -- never the std::vector storage or a stack
        // array (either would corrupt the heap on the enumerator's release).
        const size_t count = pidls.size();
        LPITEMIDLIST* items = new (std::nothrow) LPITEMIDLIST[count ? count : 1];
        if (!items)
            return E_OUTOFMEMORY;
        for (size_t i = 0; i < count; ++i)
            items[i] = pidls[i];

        const HRESULT hr = enumerator->Init(items, items + count, nullptr, AtlFlagTakeOwnership);
        if (FAILED(hr))
        {
            for (size_t i = 0; i < count; ++i)
                CoTaskMemFree(items[i]);
            delete[] items;
            return hr;
        }

        // Ownership of both the array and the PIDLs transferred to the enumerator.
        pidls.clear();
        return enumerator->QueryInterface(IID_PPV_ARGS(ppenumIDList));
    }

    LPITEMIDLIST CreateNamespaceRelativePidl(LPCITEMIDLIST absolutePidl)
    {
        const auto path = GetNamespaceRelativePath(absolutePidl);
        if (path.empty())
        {
            auto* pidl = static_cast<LPITEMIDLIST>(CoTaskMemAlloc(sizeof(USHORT)));
            if (!pidl)
                return nullptr;
            pidl->mkid.cb = 0;
            return pidl;
        }

        LPITEMIDLIST combined = nullptr;
        size_t start = 0;
        while (start < path.size())
        {
            const size_t slash = path.find('\\', start);
            const auto segment = path.substr(start, slash == std::string::npos ? std::string::npos : slash - start);
            if (!segment.empty())
            {
                CComHeapPtr<ITEMIDLIST> segmentPidl;
                segmentPidl.Attach(CreateSimplePidl(segment.c_str()));
                if (!segmentPidl)
                {
                    if (combined)
                        CoTaskMemFree(combined);
                    return nullptr;
                }

                CComHeapPtr<ITEMIDLIST> next;
                AttachShellPidl(
                    next,
                    reinterpret_cast<LPCITEMIDLIST>(combined),
                    static_cast<LPCITEMIDLIST>(segmentPidl.m_pData));
                if (!next)
                {
                    if (combined)
                        CoTaskMemFree(combined);
                    return nullptr;
                }

                if (combined)
                    CoTaskMemFree(combined);
                combined = next.Detach();
            }

            if (slash == std::string::npos)
                break;
            start = slash + 1;
        }

        return combined ? combined : CreateNamespaceRelativePidl(nullptr);
    }
}
