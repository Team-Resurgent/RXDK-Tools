#include "stdafx.h"
#include "NativeExtractIcon.h"
#include "NativeFolderOps.h"
#include "resource.h"

namespace
{
    enum class NativeIconKind
    {
        Root,
        AddConsole,
        Console,
        ConsoleDefault,
        Volume,
        Folder,
        Xbe,
        File,
    };

    bool IsXbeFile(LPCSTR fileName)
    {
        if (!fileName)
            return false;

        const char* extension = strrchr(fileName, '.');
        return extension != nullptr && _stricmp(extension + 1, "XBE") == 0;
    }

    class ATL_NO_VTABLE CNativeExtractIcon :
        public CComObjectRootEx<CComMultiThreadModel>,
        public IExtractIconW,
        public IExtractIconA
    {
    public:
        BEGIN_COM_MAP(CNativeExtractIcon)
            COM_INTERFACE_ENTRY(IExtractIconW)
            COM_INTERFACE_ENTRY(IExtractIconA)
        END_COM_MAP()

        void Initialize(NativeIconKind kind, std::string fileName = {}, DWORD fileAttributes = 0)
        {
            m_kind = kind;
            m_fileName = std::move(fileName);
            m_fileAttributes = fileAttributes;
        }

        STDMETHOD(GetIconLocation)(UINT uFlags, LPSTR szIconFile, UINT cchMax, LPINT piIndex, UINT* pwFlags)
        {
            return GetIconLocationImpl(uFlags, szIconFile, cchMax, piIndex, pwFlags);
        }

        STDMETHOD(Extract)(LPCSTR /*pszFile*/, UINT /*nIconIndex*/, HICON* phiconLarge, HICON* phiconSmall, UINT /*nIconSize*/)
        {
            if (phiconLarge)
                *phiconLarge = nullptr;
            if (phiconSmall)
                *phiconSmall = nullptr;
            return S_FALSE;
        }

        STDMETHOD(GetIconLocation)(UINT uFlags, LPWSTR szIconFile, UINT cchMax, LPINT piIndex, UINT* pwFlags)
        {
            CHAR buffer[MAX_PATH] = {};
            const UINT maxChars = cchMax > MAX_PATH ? MAX_PATH : cchMax;
            const HRESULT hr = GetIconLocationImpl(uFlags, buffer, maxChars, piIndex, pwFlags);
            if (SUCCEEDED(hr))
                StringCchPrintfW(szIconFile, cchMax, L"%hs", buffer);
            return hr;
        }

        STDMETHOD(Extract)(LPCWSTR /*pszFile*/, UINT /*nIconIndex*/, HICON* phiconLarge, HICON* phiconSmall, UINT /*nIconSize*/)
        {
            if (phiconLarge)
                *phiconLarge = nullptr;
            if (phiconSmall)
                *phiconSmall = nullptr;
            return S_FALSE;
        }

    private:
        HRESULT GetIconLocationImpl(UINT /*uFlags*/, LPSTR szIconFile, UINT cchMax, LPINT piIndex, UINT* pwFlags)
        {
            if (!szIconFile || !piIndex || !pwFlags || cchMax == 0)
                return E_INVALIDARG;

            if (m_kind == NativeIconKind::File)
            {
                SHFILEINFOA shellFileInfo = {};
                const LPCSTR fileName = m_fileName.empty() ? "file" : m_fileName.c_str();
                if (SHGetFileInfoA(
                        fileName,
                        m_fileAttributes,
                        &shellFileInfo,
                        sizeof(shellFileInfo),
                        SHGFI_USEFILEATTRIBUTES | SHGFI_SYSICONINDEX))
                {
                    *piIndex = shellFileInfo.iIcon;
                    *pwFlags = GIL_NOTFILENAME;
                    StringCchCopyA(szIconFile, cchMax, "*");
                    return S_OK;
                }

                return E_FAIL;
            }

            if (!GetModuleFileNameA(_AtlBaseModule.GetModuleInstance(), szIconFile, cchMax))
                return HRESULT_FROM_WIN32(GetLastError());

            *pwFlags = 0;
            switch (m_kind)
            {
            case NativeIconKind::AddConsole:
                *piIndex = ICON_INDEX(IDI_ADD_CONSOLE);
                break;
            case NativeIconKind::Console:
                *piIndex = ICON_INDEX(IDI_CONSOLE);
                break;
            case NativeIconKind::ConsoleDefault:
                *piIndex = ICON_INDEX(IDI_CONSOLE_DEFAULT);
                break;
            case NativeIconKind::Volume:
                *piIndex = ICON_INDEX(IDI_VOLUME);
                break;
            case NativeIconKind::Folder:
                *piIndex = ICON_INDEX(IDI_FOLDER);
                break;
            case NativeIconKind::Xbe:
                *piIndex = ICON_INDEX(IDI_XBE);
                break;
            default:
                *piIndex = ICON_INDEX(IDI_MAIN);
                break;
            }

            return S_OK;
        }

        NativeIconKind m_kind = NativeIconKind::Root;
        std::string m_fileName;
        DWORD m_fileAttributes = 0;
    };

    NativeIconKind IconKindForSelection(LPCITEMIDLIST folderPidl, UINT cidl, LPCITEMIDLIST* apidl, std::string& fileName, DWORD& fileAttributes)
    {
        fileName.clear();
        fileAttributes = 0;

        if (cidl != 1 || !apidl || !apidl[0])
            return NativeIconKind::Root;

        const auto segment = NativeFolderOps::GetLastSegment(apidl[0]);
        if (_stricmp(segment.c_str(), NativeFolderOps::kAddConsoleSegment) == 0)
            return NativeIconKind::AddConsole;

        CComHeapPtr<ITEMIDLIST> absolute;
        AttachShellPidl(absolute, folderPidl, apidl[0]);
        const UINT segments = NativeFolderOps::CountPidlSegments(absolute);

        if (segments == 2 && segment.length() == 1 && isalpha(static_cast<unsigned char>(segment[0])))
            return NativeIconKind::Volume;

        if (NativeFolderOps::ConsoleNameExists(segment.c_str()))
            return NativeFolderOps::IsDefaultConsole(segment.c_str()) ? NativeIconKind::ConsoleDefault : NativeIconKind::Console;

        if (IsXbeFile(segment.c_str()))
            return NativeIconKind::Xbe;

        if (segments >= 3 && strchr(segment.c_str(), '.') == nullptr)
            return NativeIconKind::Folder;

        fileName = segment;
        fileAttributes = FILE_ATTRIBUTE_NORMAL;
        return NativeIconKind::File;
    }
}

HRESULT CreateNativeExtractIcon(
    LPCITEMIDLIST folderPidl,
    UINT cidl,
    LPCITEMIDLIST* apidl,
    REFIID riid,
    void** ppv)
{
    if (!ppv)
        return E_POINTER;

    *ppv = nullptr;
    if (riid != IID_IExtractIcon && riid != IID_IExtractIconA && riid != IID_IExtractIconW)
        return E_NOINTERFACE;

    std::string fileName;
    DWORD fileAttributes = 0;
    const auto kind = IconKindForSelection(folderPidl, cidl, apidl, fileName, fileAttributes);

    CComObject<CNativeExtractIcon>* icon = nullptr;
    RH(CComObject<CNativeExtractIcon>::CreateInstance(&icon));
    icon->Initialize(kind, std::move(fileName), fileAttributes);
    return icon->QueryInterface(riid, ppv);
}

extern "C" HRESULT __stdcall XbShellExt_CreateNativeExtractIcon(
    LPCITEMIDLIST folderPidl,
    UINT cidl,
    LPCITEMIDLIST* apidl,
    REFIID riid,
    void** ppv)
{
    return CreateNativeExtractIcon(folderPidl, cidl, apidl, riid, ppv);
}
