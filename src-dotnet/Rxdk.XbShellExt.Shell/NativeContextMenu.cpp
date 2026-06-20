#include "stdafx.h"
#include "NativeContextMenu.h"
#include "NativeDataObject.h"
#include "NativeFolderOps.h"
#include "NativeShellNotify.h"
#include "NativeUi.h"
#include "ShellObjectSite.h"
#include "ShellTrace.h"

namespace
{
    enum class SelectionKind
    {
        Background,
        Folder,
        AddConsole,
        File,
    };

    enum class LocationKind
    {
        Root,
        Console,
        Volume,
        Directory,
    };

    enum class TargetKind
    {
        Background,
        AddConsole,
        Console,
        Volume,
        Directory,
        File,
        Xbe,
    };

    enum class CommandId : UINT
    {
        Open = 0,
        Explore,
        Properties,
        Cut,
        Copy,
        Paste,
        Delete,
        Rename,
        NewFolder,
        Launch,
        AddXbox,
        Security,
        SetDefault,
        Capture,
        RebootWarm,
        RebootSameTitle,
        RebootCold,
        RemoveConsole,
    };

    bool IsXbeFile(LPCSTR segment)
    {
        if (!segment)
            return false;

        const char* dot = strrchr(segment, '.');
        if (!dot)
            return false;

        return _stricmp(dot, ".xbe") == 0;
    }

    bool SegmentLooksBrowsable(LPCSTR segment)
    {
        if (!segment || segment[0] == '\0')
            return true;
        if (_stricmp(segment, NativeFolderOps::kAddConsoleSegment) == 0)
            return true;
        if (NativeFolderOps::ConsoleNameExists(segment))
            return true;
        if (segment[1] == '\0' && isalpha(static_cast<unsigned char>(segment[0])))
            return true;
        if (strchr(segment, '.') == nullptr)
            return true;
        return false;
    }

    bool IsDefaultConsole(LPCSTR segment)
    {
        if (!segment || segment[0] == '\0')
            return false;

        HKEY key = nullptr;
        if (RegOpenKeyExW(
                HKEY_CURRENT_USER,
                L"Software\\Microsoft\\XboxSDK\\xbshlext",
                0,
                KEY_READ,
                &key) != ERROR_SUCCESS)
        {
            return false;
        }

        char defaultName[256] = {};
        DWORD size = sizeof(defaultName);
        const LONG result = RegQueryValueExA(
            key,
            "Default",
            nullptr,
            nullptr,
            reinterpret_cast<LPBYTE>(defaultName),
            &size);
        RegCloseKey(key);
        return result == ERROR_SUCCESS && _stricmp(defaultName, segment) == 0;
    }

    LocationKind ClassifyLocation(LPCITEMIDLIST folderPidl)
    {
        const auto path = NativeFolderOps::GetNamespaceRelativePath(folderPidl);
        if (path.empty())
            return LocationKind::Root;

        if (path.find('\\') == std::string::npos)
            return LocationKind::Console;

        if (path.find('\\', path.find('\\') + 1) == std::string::npos)
            return LocationKind::Volume;

        return LocationKind::Directory;
    }

    TargetKind ClassifyTarget(LPCITEMIDLIST folderPidl, LPCSTR segment, bool hasSelection)
    {
        if (!hasSelection || !segment || segment[0] == '\0')
            return TargetKind::Background;

        if (segment[0] == '?')
            return TargetKind::AddConsole;

        if (NativeFolderOps::ConsoleNameExists(segment) &&
            ClassifyLocation(folderPidl) == LocationKind::Root)
        {
            return TargetKind::Console;
        }

        if (segment[1] == '\0' &&
            isalpha(static_cast<unsigned char>(segment[0])) &&
            ClassifyLocation(folderPidl) == LocationKind::Console)
        {
            return TargetKind::Volume;
        }

        if (IsXbeFile(segment))
            return TargetKind::Xbe;

        if (SegmentLooksBrowsable(segment))
            return TargetKind::Directory;

        return TargetKind::File;
    }

    bool ShouldOfferBrowseTarget(TargetKind target)
    {
        return target == TargetKind::AddConsole ||
            target == TargetKind::Console ||
            target == TargetKind::Volume ||
            target == TargetKind::Directory;
    }

    int CommandToManagedId(CommandId command)
    {
        switch (command)
        {
        case CommandId::Properties:
            return 0;
        case CommandId::Security:
            return 1;
        case CommandId::AddXbox:
            return 2;
        case CommandId::RebootWarm:
            return 3;
        case CommandId::RebootSameTitle:
            return 4;
        case CommandId::RebootCold:
            return 5;
        case CommandId::Capture:
            return 6;
        case CommandId::SetDefault:
            return 7;
        case CommandId::Cut:
            return 8;
        case CommandId::Copy:
            return 9;
        case CommandId::Paste:
            return 10;
        case CommandId::Delete:
            return 11;
        case CommandId::Rename:
            return 12;
        case CommandId::NewFolder:
            return 13;
        case CommandId::Launch:
            return 14;
        case CommandId::RemoveConsole:
            return 15;
        default:
            return -1;
        }
    }

    bool IsManagedCommand(CommandId command)
    {
        return command != CommandId::Open && command != CommandId::Explore;
    }

    HRESULT LaunchExplorerToNamespacePath(LPCITEMIDLIST absolutePidl, bool explore)
    {
        if (!absolutePidl)
            return E_INVALIDARG;

        const auto relative = NativeFolderOps::GetNamespaceRelativePath(absolutePidl);
        wchar_t args[512] = {};
        if (relative.empty())
        {
            StringCchPrintfW(
                args,
                ARRAYSIZE(args),
                L"%s::{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}",
                explore ? L"/e,/root," : L"/root,");
        }
        else
        {
            StringCchPrintfW(
                args,
                ARRAYSIZE(args),
                L"%s::{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}\\%hs",
                explore ? L"/e,/root," : L"/root,",
                relative.c_str());
        }

        const HINSTANCE result = ShellExecuteW(
            nullptr,
            L"open",
            L"explorer.exe",
            args,
            nullptr,
            SW_SHOWNORMAL);
        return reinterpret_cast<INT_PTR>(result) > 32 ? S_OK : HRESULT_FROM_WIN32(GetLastError());
    }

    bool IsConsoleOnlySelection(LPCITEMIDLIST folderPidl, LPCSTR segment)
    {
        if (!segment || segment[0] == '\0')
            return false;
        if (_stricmp(segment, NativeFolderOps::kAddConsoleSegment) == 0)
            return false;
        if (!NativeFolderOps::ConsoleNameExists(segment))
            return false;

        return NativeFolderOps::CountPidlSegments(folderPidl) == 0;
    }

    bool CanPasteFromClipboard()
    {
        OleInitialize(nullptr);

        IDataObject* pDataObject = nullptr;
        const HRESULT hr = OleGetClipboard(&pDataObject);
        bool canPaste = false;
        if (SUCCEEDED(hr) && pDataObject)
        {
            static const CLIPFORMAT cfXbox =
                static_cast<CLIPFORMAT>(RegisterClipboardFormatA("XBOX_FILEDESCRIPTOR"));
            static const CLIPFORMAT cfFileDescW =
                static_cast<CLIPFORMAT>(RegisterClipboardFormatW(CFSTR_FILEDESCRIPTORW));
            const CLIPFORMAT formats[] = { CF_HDROP, cfXbox, cfFileDescW };

            for (const CLIPFORMAT cf : formats)
            {
                FORMATETC etc = {};
                etc.cfFormat = cf;
                etc.dwAspect = DVASPECT_CONTENT;
                etc.lindex = -1;
                etc.tymed = TYMED_HGLOBAL;
                if (SUCCEEDED(pDataObject->QueryGetData(&etc)))
                {
                    canPaste = true;
                    break;
                }
            }

            pDataObject->Release();
        }

        return canPaste;
    }

    HRESULT SetPreferredDropEffect(IDataObject* dataObject, DWORD effect)
    {
        if (!dataObject)
            return E_POINTER;

        static const CLIPFORMAT cfPreferred =
            static_cast<CLIPFORMAT>(RegisterClipboardFormat(CFSTR_PREFERREDDROPEFFECT));

        HGLOBAL hGlobal = GlobalAlloc(GPTR, sizeof(DWORD));
        if (!hGlobal)
            return E_OUTOFMEMORY;

        *static_cast<DWORD*>(GlobalLock(hGlobal)) = effect;
        GlobalUnlock(hGlobal);

        FORMATETC etc = {};
        etc.cfFormat = cfPreferred;
        etc.dwAspect = DVASPECT_CONTENT;
        etc.lindex = -1;
        etc.tymed = TYMED_HGLOBAL;

        STGMEDIUM medium = {};
        medium.tymed = TYMED_HGLOBAL;
        medium.hGlobal = hGlobal;

        return dataObject->SetData(&etc, &medium, TRUE);
    }

    // Cut/copy must run OleSetClipboard on Explorer's STA thread (legacy xbshlext
    // menu.cpp). Doing it from the managed comhost STA deadlocks DefView.
    HRESULT CutCopyOnExplorerThread(
        CShellObjectWithSite& site,
        LPCITEMIDLIST folderPidl,
        LPCITEMIDLIST childPidl,
        HWND hwnd,
        bool fCut)
    {
        XB_TRACE_SCOPE("CtxMenu.CutCopy");
        if (!childPidl)
            return E_FAIL;

        const HRESULT hrInit = OleInitialize(nullptr);
        UNREFERENCED_PARAMETER(hrInit);

        CComPtr<IDataObject> dataObject;
        LPCITEMIDLIST child = childPidl;
        HRESULT hr = CreateNativeDataObject(folderPidl, 1, &child, IID_PPV_ARGS(&dataObject));
        if (FAILED(hr) || !dataObject)
        {
            __xbTraceScope.Note("CreateNativeDataObject hr=0x%08X", hr);
            return FAILED(hr) ? hr : E_FAIL;
        }

        SetPreferredDropEffect(dataObject, fCut ? DROPEFFECT_MOVE : DROPEFFECT_COPY);

        CComPtr<IShellFolderView> shellFolderView;
        if (SUCCEEDED(site.GetSite(IID_PPV_ARGS(&shellFolderView))) && shellFolderView)
            shellFolderView->SetPoints(dataObject);

        hr = OleSetClipboard(dataObject);
        if (SUCCEEDED(hr) && shellFolderView)
            shellFolderView->SetClipboard(fCut);

        __xbTraceScope.Note("OleSetClipboard hr=0x%08X cut=%d", hr, fCut ? 1 : 0);

        return hr;
    }

    bool ShouldInvokeContextCommandSynchronously(CommandId command)
    {
        switch (command)
        {
        case CommandId::Delete:
        case CommandId::Launch:
        case CommandId::NewFolder:
        case CommandId::SetDefault:
        case CommandId::RemoveConsole:
        case CommandId::RebootWarm:
        case CommandId::RebootSameTitle:
        case CommandId::RebootCold:
            return true;
        default:
            return false;
        }
    }

    HRESULT InvokeManagedContextCommandSync(
        LPCITEMIDLIST folderPidl,
        LPCITEMIDLIST childPidl,
        HWND hwnd,
        CommandId command)
    {
        const int managedId = CommandToManagedId(command);
        if (managedId < 0)
            return E_INVALIDARG;

        CComPtr<IShellFolder> managed;
        HRESULT hr = CreateManagedFolder(IID_PPV_ARGS(&managed));
        if (FAILED(hr) || !managed)
            return hr;

        if (folderPidl)
        {
            CComPtr<IPersistFolder> persist;
            if (SUCCEEDED(managed->QueryInterface(IID_PPV_ARGS(&persist))) && persist)
                persist->Initialize(folderPidl);
        }

        CComPtr<IXboxShellExtUi> ui;
        hr = managed->QueryInterface(IID_PPV_ARGS(&ui));
        if (FAILED(hr) || !ui)
            return hr;

        hr = ui->InvokeContextCommand(hwnd, childPidl, managedId);
        if (SUCCEEDED(hr) && ShouldRefreshFolderAfterContextCommand(managedId))
            NotifyFolderContentsChanged(folderPidl, nullptr);
        return hr;
    }

    enum class ManagedUiAction
    {
        Properties,
        Security,
        AddXbox,
        ContextCommand,
    };

    struct ManagedUiLaunchData
    {
        CComHeapPtr<ITEMIDLIST> folderPidl;
        CComHeapPtr<ITEMIDLIST> childPidl;
        HWND hwnd = nullptr;
        ManagedUiAction action = ManagedUiAction::Properties;
        int commandId = -1;
    };

    DWORD WINAPI ManagedUiLaunchThread(LPVOID param)
    {
        XB_TRACE_SCOPE("CtxMenu.ManagedUiLaunch");
        std::unique_ptr<ManagedUiLaunchData> data(static_cast<ManagedUiLaunchData*>(param));

        CComPtr<IShellFolder> managed;
        HRESULT hr = CreateManagedFolder(IID_PPV_ARGS(&managed));
        if (FAILED(hr) || !managed)
        {
            __xbTraceScope.Note("CreateManagedFolder hr=0x%08X", hr);
            return 0;
        }

        if (data->folderPidl)
        {
            CComPtr<IPersistFolder> persist;
            if (SUCCEEDED(managed->QueryInterface(IID_PPV_ARGS(&persist))) && persist)
                persist->Initialize(data->folderPidl);
        }

        CComPtr<IXboxShellExtUi> ui;
        hr = managed->QueryInterface(IID_PPV_ARGS(&ui));
        if (FAILED(hr) || !ui)
        {
            __xbTraceScope.Note("QueryInterface IXboxShellExtUi hr=0x%08X", hr);
            MessageBoxW(
                data->hwnd,
                L"Could not open the Xbox Neighborhood property UI.",
                L"Xbox Neighborhood",
                MB_OK | MB_ICONWARNING);
            return 0;
        }

        switch (data->action)
        {
        case ManagedUiAction::Security:
            hr = ui->ShowSecurityForSelection(data->hwnd);
            break;
        case ManagedUiAction::AddXbox:
            hr = ui->ShowAddConsoleWizard(data->hwnd);
            break;
        case ManagedUiAction::ContextCommand:
            hr = ui->InvokeContextCommand(data->hwnd, data->childPidl, data->commandId);
            break;
        default:
            hr = ui->ShowPropertiesForSelection(data->hwnd, data->childPidl);
            break;
        }

        __xbTraceScope.Note("managed UI action=%d hr=0x%08X", static_cast<int>(data->action), hr);
        if (SUCCEEDED(hr) &&
            data->action == ManagedUiAction::ContextCommand &&
            ShouldRefreshFolderAfterContextCommand(data->commandId))
        {
            NotifyFolderContentsChanged(data->folderPidl, nullptr);
        }

        if (FAILED(hr) &&
            (data->action == ManagedUiAction::Properties || data->action == ManagedUiAction::Security))
        {
            MessageBoxW(
                data->hwnd,
                L"Could not open properties for the current selection.",
                L"Xbox Neighborhood",
                MB_OK | MB_ICONWARNING);
        }
        return 0;
    }

    HRESULT LaunchManagedUi(
        LPCITEMIDLIST folderPidl,
        LPCITEMIDLIST childPidl,
        HWND hwnd,
        ManagedUiAction action,
        int commandId = -1)
    {
        std::unique_ptr<ManagedUiLaunchData> data(new (std::nothrow) ManagedUiLaunchData());
        if (!data)
            return E_OUTOFMEMORY;

        data->hwnd = hwnd;
        data->action = action;
        data->commandId = commandId;
        if (folderPidl)
            AttachShellPidl(data->folderPidl, folderPidl);
        if (childPidl)
            AttachShellPidl(data->childPidl, childPidl);

        if (!SHCreateThread(ManagedUiLaunchThread, data.get(), CTF_COINIT | CTF_PROCESS_REF, nullptr))
            return HRESULT_FROM_WIN32(GetLastError());

        data.release();
        return S_OK;
    }

    HRESULT LaunchManagedContextCommand(
        LPCITEMIDLIST folderPidl,
        LPCITEMIDLIST childPidl,
        HWND hwnd,
        CommandId command)
    {
        const int managedId = CommandToManagedId(command);
        if (managedId < 0)
            return E_INVALIDARG;

        return LaunchManagedUi(folderPidl, childPidl, hwnd, ManagedUiAction::ContextCommand, managedId);
    }

    const char* VerbForCommandId(CommandId command)
    {
        switch (command)
        {
        case CommandId::Open:
            return "open";
        case CommandId::Explore:
            return "explore";
        case CommandId::Properties:
            return "properties";
        case CommandId::Cut:
            return "cut";
        case CommandId::Copy:
            return "copy";
        case CommandId::Paste:
            return "paste";
        case CommandId::Delete:
            return "delete";
        case CommandId::Rename:
            return "rename";
        case CommandId::NewFolder:
            return "newfolder";
        case CommandId::Launch:
            return "launch";
        case CommandId::AddXbox:
            return "addxbox";
        case CommandId::Security:
            return "security";
        case CommandId::SetDefault:
            return "setdefault";
        case CommandId::Capture:
            return "capture";
        case CommandId::RebootWarm:
            return "rebootwarm";
        case CommandId::RebootSameTitle:
            return "rebootsametitle";
        case CommandId::RebootCold:
            return "rebootcold";
        case CommandId::RemoveConsole:
            return "removeconsole";
        default:
            return nullptr;
        }
    }

    class ATL_NO_VTABLE CNativeContextMenu :
        public CComObjectRootEx<CComMultiThreadModel>,
        public CShellObjectWithSite,
        public IContextMenu3
    {
    public:
        BEGIN_COM_MAP(CNativeContextMenu)
            COM_INTERFACE_ENTRY(IObjectWithSite)
            COM_INTERFACE_ENTRY(IContextMenu)
            COM_INTERFACE_ENTRY(IContextMenu2)
            COM_INTERFACE_ENTRY(IContextMenu3)
        END_COM_MAP()

        void Initialize(LPCITEMIDLIST folderPidl, UINT cidl, LPCITEMIDLIST* apidl)
        {
            m_commands.clear();
            m_folderPidl.Free();
            if (folderPidl)
                AttachShellPidl(m_folderPidl, folderPidl);

            m_childPidl.Free();
            m_selectionSegment.clear();
            m_hasSelection = false;
            m_consoleOnly = false;
            m_location = ClassifyLocation(folderPidl);
            m_target = TargetKind::Background;

            if (cidl == 0)
            {
                m_kind = SelectionKind::Background;
                return;
            }

            if (cidl == 1 && apidl && apidl[0])
            {
                m_hasSelection = true;
                AttachShellPidl(m_childPidl, apidl[0]);
                m_selectionSegment = NativeFolderOps::GetLastSegment(apidl[0]);
                m_target = ClassifyTarget(folderPidl, m_selectionSegment.c_str(), true);
                m_consoleOnly = m_target == TargetKind::Console;

                if (m_target == TargetKind::AddConsole)
                    m_kind = SelectionKind::AddConsole;
                else if (ShouldOfferBrowseTarget(m_target))
                    m_kind = SelectionKind::Folder;
                else
                    m_kind = SelectionKind::File;
                return;
            }

            m_kind = SelectionKind::File;
            m_target = TargetKind::File;
        }

        STDMETHOD(QueryContextMenu)(HMENU hmenu, UINT indexMenu, UINT idCmdFirst, UINT idCmdLast, UINT uFlags)
        {
            XB_TRACE_SCOPE("CtxMenu.QueryContextMenu");
            UNREFERENCED_PARAMETER(indexMenu);
            if (!hmenu || idCmdFirst > idCmdLast)
                return E_INVALIDARG;

            m_idCmdFirst = idCmdFirst;
            m_commands.clear();
            UINT nextId = idCmdFirst;
            UINT defaultId = static_cast<UINT>(-1);
            const bool canRename = (uFlags & CMF_CANRENAME) != 0;

            auto appendItem = [&](LPCWSTR label, CommandId command, bool makeDefault = false, bool disabled = false) -> HRESULT
            {
                if (nextId > idCmdLast)
                    return E_FAIL;

                UINT flags = MF_STRING | MF_BYPOSITION;
                if (disabled)
                    flags |= MF_GRAYED;

                if (!AppendMenuW(hmenu, flags, nextId, label))
                    return HRESULT_FROM_WIN32(GetLastError());

                m_commands.push_back(command);
                if (makeDefault || defaultId == static_cast<UINT>(-1))
                    defaultId = nextId;
                nextId++;
                return S_OK;
            };

            auto appendSeparator = [&]() -> HRESULT
            {
                if (nextId > idCmdLast)
                    return E_FAIL;

                if (!AppendMenuW(hmenu, MF_SEPARATOR | MF_BYPOSITION, 0, nullptr))
                    return HRESULT_FROM_WIN32(GetLastError());
                return S_OK;
            };

            auto appendRebootSubmenu = [&]() -> HRESULT
            {
                if (nextId + 2 > idCmdLast)
                    return E_FAIL;

                HMENU rebootMenu = CreatePopupMenu();
                if (!rebootMenu)
                    return E_OUTOFMEMORY;

                const UINT warmId = nextId;
                if (!AppendMenuW(rebootMenu, MF_STRING, nextId++, L"&Warm Reboot"))
                    return HRESULT_FROM_WIN32(GetLastError());
                m_commands.push_back(CommandId::RebootWarm);

                if (!AppendMenuW(rebootMenu, MF_STRING, nextId++, L"Reboot Same &Title"))
                    return HRESULT_FROM_WIN32(GetLastError());
                m_commands.push_back(CommandId::RebootSameTitle);

                if (!AppendMenuW(rebootMenu, MF_STRING, nextId++, L"&Cold Reboot"))
                    return HRESULT_FROM_WIN32(GetLastError());
                m_commands.push_back(CommandId::RebootCold);

                if (!AppendMenuW(hmenu, MF_POPUP | MF_BYPOSITION, reinterpret_cast<UINT_PTR>(rebootMenu), L"&Reboot"))
                    return HRESULT_FROM_WIN32(GetLastError());

                return S_OK;
            };

            const bool pasteAvailable = CanPasteFromClipboard();

            RH(appendItem(L"&Open", CommandId::Open, m_target != TargetKind::Xbe));

            if (ShouldOfferBrowseTarget(m_target))
                RH(appendItem(L"E&xplore", CommandId::Explore));

            switch (m_target)
            {
            case TargetKind::AddConsole:
                break;

            case TargetKind::Console:
                RH(appendSeparator());
                RH(appendRebootSubmenu());
                RH(appendItem(L"&Capture", CommandId::Capture));
                if (!IsDefaultConsole(m_selectionSegment.c_str()))
                    RH(appendItem(L"Set &Default", CommandId::SetDefault));
                RH(appendItem(L"&Security", CommandId::Security));
                RH(appendItem(L"&Delete", CommandId::RemoveConsole));
                RH(appendSeparator());
                RH(appendItem(L"&Properties", CommandId::Properties));
                break;

            case TargetKind::Volume:
                RH(appendSeparator());
                RH(appendItem(L"&Properties", CommandId::Properties));
                break;

            case TargetKind::Directory:
                RH(appendSeparator());
                RH(appendItem(L"Cu&t", CommandId::Cut));
                RH(appendItem(L"&Copy", CommandId::Copy));
                RH(appendItem(L"&Paste", CommandId::Paste, false, !pasteAvailable));
                RH(appendItem(L"&Delete", CommandId::Delete));
                if (canRename)
                    RH(appendItem(L"&Rename", CommandId::Rename));
                RH(appendItem(L"&New Folder", CommandId::NewFolder));
                RH(appendSeparator());
                RH(appendItem(L"&Properties", CommandId::Properties));
                break;

            case TargetKind::File:
                RH(appendSeparator());
                RH(appendItem(L"Cu&t", CommandId::Cut));
                RH(appendItem(L"&Copy", CommandId::Copy));
                RH(appendSeparator());
                RH(appendItem(L"&Delete", CommandId::Delete));
                if (canRename)
                    RH(appendItem(L"&Rename", CommandId::Rename));
                RH(appendSeparator());
                RH(appendItem(L"&Properties", CommandId::Properties));
                break;

            case TargetKind::Xbe:
                defaultId = static_cast<UINT>(-1);
                RH(appendItem(L"&Launch", CommandId::Launch, true));
                RH(appendSeparator());
                RH(appendItem(L"Cu&t", CommandId::Cut));
                RH(appendItem(L"&Copy", CommandId::Copy));
                RH(appendSeparator());
                RH(appendItem(L"&Delete", CommandId::Delete));
                if (canRename)
                    RH(appendItem(L"&Rename", CommandId::Rename));
                RH(appendSeparator());
                RH(appendItem(L"&Properties", CommandId::Properties));
                break;

            case TargetKind::Background:
                if (m_location == LocationKind::Volume || m_location == LocationKind::Directory)
                {
                    RH(appendItem(L"&Paste", CommandId::Paste, false, !pasteAvailable));
                    RH(appendItem(L"&New Folder", CommandId::NewFolder));
                    RH(appendSeparator());
                }

                if (OfferBackgroundProperties())
                    RH(appendItem(L"&Properties", CommandId::Properties));
                break;
            }

            if (defaultId != static_cast<UINT>(-1))
                SetMenuDefaultItem(hmenu, defaultId, FALSE);

            return static_cast<HRESULT>(nextId - idCmdFirst);
        }

        STDMETHOD(HandleMenuMsg)(UINT /*uMsg*/, WPARAM /*wParam*/, LPARAM /*lParam*/)
        {
            return S_OK;
        }

        STDMETHOD(HandleMenuMsg2)(UINT /*uMsg*/, WPARAM /*wParam*/, LPARAM /*lParam*/, LRESULT* plResult)
        {
            if (plResult)
                *plResult = 0;
            return S_OK;
        }

        STDMETHOD(InvokeCommand)(LPCMINVOKECOMMANDINFO pici)
        {
            XB_TRACE_SCOPE("CtxMenu.InvokeCommand");
            if (!pici)
                return E_INVALIDARG;

            const CommandId command = ResolveCommand(pici);
            __xbTraceScope.Note(
                "command=%d target=%d hasSelection=%d",
                static_cast<int>(command),
                static_cast<int>(m_target),
                m_hasSelection ? 1 : 0);

            switch (command)
            {
            case CommandId::Open:
                if (m_target == TargetKind::AddConsole)
                    return LaunchManagedUi(m_folderPidl, m_childPidl, pici->hwnd, ManagedUiAction::AddXbox);
                if (ShouldOfferBrowseTarget(m_target))
                    return Navigate(pici, false);
                return S_OK;
            case CommandId::Explore:
                if (ShouldOfferBrowseTarget(m_target))
                    return Navigate(pici, true);
                return S_OK;
            case CommandId::Properties:
                return LaunchManagedUi(m_folderPidl, m_childPidl, pici->hwnd, ManagedUiAction::Properties);
            case CommandId::AddXbox:
                return LaunchManagedUi(m_folderPidl, m_childPidl, pici->hwnd, ManagedUiAction::AddXbox);
            case CommandId::Security:
                return LaunchManagedUi(m_folderPidl, nullptr, pici->hwnd, ManagedUiAction::Security);
            case CommandId::Cut:
                return CutCopyOnExplorerThread(*this, m_folderPidl, m_childPidl, pici->hwnd, true);
            case CommandId::Copy:
                return CutCopyOnExplorerThread(*this, m_folderPidl, m_childPidl, pici->hwnd, false);
            case CommandId::Paste:
                if (!CanPasteFromClipboard())
                {
                    MessageBoxW(
                        pici->hwnd,
                        L"There is nothing to paste.",
                        L"Xbox Neighborhood",
                        MB_OK | MB_ICONINFORMATION);
                    return E_FAIL;
                }
                return InvokeManagedContextCommandSync(m_folderPidl, m_childPidl, pici->hwnd, CommandId::Paste);
            default:
                if (IsManagedCommand(command))
                {
                    if (ShouldInvokeContextCommandSynchronously(command))
                        return InvokeManagedContextCommandSync(m_folderPidl, m_childPidl, pici->hwnd, command);
                    return LaunchManagedContextCommand(m_folderPidl, m_childPidl, pici->hwnd, command);
                }
                return E_INVALIDARG;
            }
        }

        STDMETHOD(GetCommandString)(UINT_PTR idCmd, UINT uFlags, UINT* /*pwReserved*/, LPSTR pszName, UINT cchMax)
        {
            if (!pszName || cchMax == 0)
                return E_INVALIDARG;

            UINT offset = static_cast<UINT>(idCmd);
            if (offset >= m_idCmdFirst)
                offset -= m_idCmdFirst;

            const char* verb = VerbForOffset(offset);
            if (!verb)
                return E_INVALIDARG;

            switch (uFlags)
            {
            case GCS_VERBA:
            case GCS_HELPTEXTA:
            case GCS_VALIDATEA:
                StringCchCopyA(pszName, cchMax, verb);
                return S_OK;
            case GCS_VERBW:
            case GCS_HELPTEXTW:
            case GCS_VALIDATEW:
                StringCchPrintfW(reinterpret_cast<LPWSTR>(pszName), cchMax, L"%hs", verb);
                return S_OK;
            default:
                return E_NOTIMPL;
            }
        }

    private:
        bool OfferBackgroundProperties() const
    {
        if (m_hasSelection || !m_folderPidl)
            return false;

        return !NativeFolderOps::GetNamespaceRelativePath(m_folderPidl).empty();
    }

    bool ShouldOfferOpen() const
        {
            return m_kind == SelectionKind::Background ||
                m_kind == SelectionKind::Folder ||
                m_kind == SelectionKind::AddConsole;
        }

        CommandId ResolveCommand(LPCMINVOKECOMMANDINFO pici) const
        {
            auto resolveVerbA = [](LPCSTR verb) -> CommandId
            {
                if (!verb)
                    return static_cast<CommandId>(-1);

                if (_stricmp(verb, "open") == 0)
                    return CommandId::Open;
                if (_stricmp(verb, "explore") == 0)
                    return CommandId::Explore;
                if (_stricmp(verb, "properties") == 0)
                    return CommandId::Properties;
                if (_stricmp(verb, "cut") == 0)
                    return CommandId::Cut;
                if (_stricmp(verb, "copy") == 0)
                    return CommandId::Copy;
                if (_stricmp(verb, "paste") == 0)
                    return CommandId::Paste;
                if (_stricmp(verb, "delete") == 0)
                    return CommandId::Delete;
                if (_stricmp(verb, "rename") == 0)
                    return CommandId::Rename;
                if (_stricmp(verb, "newfolder") == 0)
                    return CommandId::NewFolder;
                if (_stricmp(verb, "launch") == 0)
                    return CommandId::Launch;
                if (_stricmp(verb, "addxbox") == 0)
                    return CommandId::AddXbox;
                if (_stricmp(verb, "security") == 0)
                    return CommandId::Security;
                if (_stricmp(verb, "setdefault") == 0)
                    return CommandId::SetDefault;
                if (_stricmp(verb, "capture") == 0)
                    return CommandId::Capture;
                if (_stricmp(verb, "rebootwarm") == 0)
                    return CommandId::RebootWarm;
                if (_stricmp(verb, "rebootsametitle") == 0)
                    return CommandId::RebootSameTitle;
                if (_stricmp(verb, "rebootcold") == 0)
                    return CommandId::RebootCold;
                if (_stricmp(verb, "removeconsole") == 0)
                    return CommandId::RemoveConsole;
                return static_cast<CommandId>(-1);
            };

            if (pici->cbSize >= sizeof(CMINVOKECOMMANDINFOEX))
            {
                const auto* piciEx = reinterpret_cast<LPCMINVOKECOMMANDINFOEX>(pici);
                if ((piciEx->fMask & CMIC_MASK_UNICODE) != 0 &&
                    piciEx->lpVerbW != nullptr &&
                    piciEx->lpVerbW[0] != L'\0')
                {
                    char narrow[64] = {};
                    WideCharToMultiByte(
                        CP_ACP,
                        0,
                        piciEx->lpVerbW,
                        -1,
                        narrow,
                        static_cast<int>(sizeof(narrow)),
                        nullptr,
                        nullptr);
                    const CommandId verbCommand = resolveVerbA(narrow);
                    if (verbCommand != static_cast<CommandId>(-1))
                        return verbCommand;
                }
            }

            if (HIWORD(pici->lpVerb) != 0)
            {
                const CommandId verbCommand = resolveVerbA(pici->lpVerb);
                if (verbCommand != static_cast<CommandId>(-1))
                    return verbCommand;
                return static_cast<CommandId>(-1);
            }

            const UINT menuId = LOWORD(pici->lpVerb);
            if (menuId >= m_idCmdFirst)
                return CommandForOffset(menuId - m_idCmdFirst);

            return CommandForOffset(menuId);
        }

        CommandId CommandForOffset(UINT offset) const
        {
            if (offset >= m_commands.size())
                return static_cast<CommandId>(-1);
            return m_commands[offset];
        }

        const char* VerbForOffset(UINT offset) const
        {
            const CommandId command = CommandForOffset(offset);
            if (command == static_cast<CommandId>(-1))
                return nullptr;
            return VerbForCommandId(command);
        }

        HRESULT Navigate(LPCMINVOKECOMMANDINFO pici, bool explore)
        {
            XB_TRACE_SCOPE("CtxMenu.Navigate");
            if (!m_childPidl)
                return E_FAIL;

            const UINT browseFlags = explore
                ? (SBSP_NEWBROWSER | SBSP_EXPLOREMODE | SBSP_RELATIVE)
                : (SBSP_DEFBROWSER | SBSP_OPENMODE | SBSP_RELATIVE);

            CComPtr<IShellBrowser> browser;
            if (SUCCEEDED(GetService(SID_SShellBrowser, IID_PPV_ARGS(&browser))) && browser)
            {
                __xbTraceScope.Note(
                    "BrowseObject relative segment=%s explore=%d",
                    m_selectionSegment.c_str(),
                    explore ? 1 : 0);
                HRESULT hr = browser->BrowseObject(m_childPidl, browseFlags);
                if (SUCCEEDED(hr))
                    return hr;

                __xbTraceScope.Note("BrowseObject relative failed hr=0x%08X", hr);
            }

            if (!m_folderPidl)
                return E_FAIL;

            CComHeapPtr<ITEMIDLIST> absolute;
            AttachShellPidl(absolute, m_folderPidl, m_childPidl);
            if (!absolute)
                return E_OUTOFMEMORY;

            if (browser)
            {
                __xbTraceScope.Note("BrowseObject absolute segment=%s explore=%d", m_selectionSegment.c_str(), explore ? 1 : 0);
                const HRESULT hr = browser->BrowseObject(absolute, browseFlags);
                if (SUCCEEDED(hr))
                    return hr;
                __xbTraceScope.Note("BrowseObject absolute failed hr=0x%08X", hr);
            }

            __xbTraceScope.Note(
                "LaunchExplorerToNamespacePath segment=%s explore=%d",
                m_selectionSegment.c_str(),
                explore ? 1 : 0);
            return LaunchExplorerToNamespacePath(absolute, explore);
        }

        SelectionKind m_kind = SelectionKind::Background;
        TargetKind m_target = TargetKind::Background;
        LocationKind m_location = LocationKind::Root;
        bool m_hasSelection = false;
        bool m_consoleOnly = false;
        UINT m_idCmdFirst = 0;
        std::vector<CommandId> m_commands;
        std::string m_selectionSegment;
        CComHeapPtr<ITEMIDLIST> m_folderPidl;
        CComHeapPtr<ITEMIDLIST> m_childPidl;
    };
}

HRESULT CreateNativeContextMenu(LPCITEMIDLIST folderPidl, UINT cidl, LPCITEMIDLIST* apidl, REFIID riid, void** ppv)
{
    if (!ppv)
        return E_POINTER;

    *ppv = nullptr;
    if (riid != IID_IContextMenu && riid != IID_IContextMenu2 && riid != IID_IContextMenu3)
        return E_NOINTERFACE;

    CComObject<CNativeContextMenu>* menu = nullptr;
    RH(CComObject<CNativeContextMenu>::CreateInstance(&menu));
    menu->Initialize(folderPidl, cidl, apidl);
    return menu->QueryInterface(riid, ppv);
}

extern "C" HRESULT __stdcall XbShellExt_CreateNativeContextMenu(
    LPCITEMIDLIST folderPidl,
    UINT cidl,
    LPCITEMIDLIST* apidl,
    REFIID riid,
    void** ppv)
{
    return CreateNativeContextMenu(folderPidl, cidl, apidl, riid, ppv);
}
