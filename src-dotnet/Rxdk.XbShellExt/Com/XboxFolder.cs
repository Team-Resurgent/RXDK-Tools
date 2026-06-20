using System.Runtime.InteropServices;
using Rxdk.XbShellExt.Diagnostics;
using Rxdk.XbShellExt.Interop;
using Rxdk.XbShellExt.Shell;
using Rxdk.XbShellExt.Ui;
using Rxdk.Xbdm.KitServices.Services;
using Rxdk.Xbdm.Managed;

using OleIDataObject = Rxdk.XbShellExt.Interop.IDataObject;

namespace Rxdk.XbShellExt.Com;

[ComVisible(true)]
[Guid(ComGuids.XboxFolderManaged)]
[ComDefaultInterface(typeof(IShellFolder2))]
[ClassInterface(ClassInterfaceType.None)]
public sealed class XboxFolder : IShellFolder, IShellFolder2, IPersistFolder, IPersistFolder2, IXboxShellExtUi
{
    [ComRegisterFunction]
    public static void Register(Type type) => ComRegistration.Register(type);

    [ComUnregisterFunction]
    public static void Unregister(Type type) => ComRegistration.Unregister(type);

    private nint _folderPidl;
    private nint _rootPidl;
    private string _fullPath = string.Empty;
    private Dictionary<string, XboxShellItem>? _childCache;
    private IReadOnlyList<nint>? _dragPidls;
    private IReadOnlyList<XboxDragEntry>? _dragCatalog;
    private XboxDragTransferSession? _dragTransferSession;

    internal string FullPath => _fullPath;

    public int GetClassID(out Guid pClassID)
    {
        pClassID = new Guid(ComGuids.XboxFolder);
        return HResults.Ok;
    }

    public int Initialize(nint pidl)
    {
        _rootPidl = PidlHelper.Clone(pidl);
        _folderPidl = PidlHelper.Clone(pidl);
        _fullPath = PidlHelper.GetNamespaceRelativePath(pidl);
        ManagedTrace.Line($"XboxFolder.Initialize fullPath='{_fullPath}'");
        return HResults.Ok;
    }

    public int GetCurFolder(out nint ppidl)
    {
        ppidl = _folderPidl == 0 ? PidlHelper.CreateEmpty() : PidlHelper.Clone(_folderPidl);
        return ppidl == 0 ? HResults.OutOfMemory : HResults.Ok;
    }

    internal int GetThisIdList(out nint ppidl)
    {
        ppidl = 0;
        if (_folderPidl == 0)
            return HResults.NoObject;

        ppidl = PidlHelper.BuildNamespaceRelativePidl(_fullPath);
        return ppidl == 0 ? HResults.OutOfMemory : HResults.Ok;
    }

    internal int GetCurFolderAbsolute(out nint ppidl)
    {
        ppidl = 0;
        if (_folderPidl == 0)
            return HResults.NoObject;

        ppidl = PidlHelper.Clone(_folderPidl);
        return ppidl == 0 ? HResults.OutOfMemory : HResults.Ok;
    }

    public int ParseDisplayName(nint hwnd, nint pbc, string pszDisplayName, out uint pchEaten, out nint ppidl, out uint pdwAttributes)
    {
        pchEaten = 0;
        ppidl = 0;
        pdwAttributes = 0;
        return HResults.NotImpl;
    }

    public int EnumObjects(nint hwnd, uint grfFlags, out IEnumIDList? ppEnumIDList)
    {
        ppEnumIDList = null;
        ManagedTrace.Line($"XboxFolder.EnumObjects fullPath='{_fullPath}' grfFlags=0x{grfFlags:X}");
        try
        {
            var children = XboxShellItemFactory.ListChildren(_fullPath);
            _childCache = new Dictionary<string, XboxShellItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in children)
                _childCache[child.Segment] = child;

            var pidls = new List<nint>();
            foreach (var child in children)
            {
                if (!MatchesFlags(child, grfFlags))
                    continue;

                pidls.Add(PidlHelper.CreateSimple(child.Segment));
            }

            ManagedTrace.Line($"XboxFolder.EnumObjects fullPath='{_fullPath}' produced {pidls.Count} item(s)");
            ppEnumIDList = new XboxEnumIdList(pidls);
            return HResults.Ok;
        }
        catch (Exception ex)
        {
            ManagedTrace.Line($"XboxFolder.EnumObjects fullPath='{_fullPath}' threw {ex.GetType().Name}: {ex.Message}");
            if (ex.StackTrace != null)
                ManagedTrace.Line(ex.StackTrace);
            if (string.IsNullOrEmpty(_fullPath))
            {
                ppEnumIDList = new XboxEnumIdList(new[] { PidlHelper.CreateSimple(ShellConstants.AddConsoleSegment) });
                return HResults.Ok;
            }

            return HResults.NoObject;
        }
    }

    public int BindToObject(nint pidl, nint pbc, ref Guid riid, out nint ppv)
    {
        ppv = 0;
        try
        {
            var segment = PidlHelper.GetLastSegment(pidl);
            if (segment.Length > 0 && segment[0] == '?')
                return HResults.NoObject;

            if (!TryGetBrowsableChild(segment, out _))
                return HResults.NoObject;

            var childPath = BuildChildPath(_fullPath, segment);
            var folder = new XboxFolder
            {
                _rootPidl = PidlHelper.Clone(_rootPidl != 0 ? _rootPidl : _folderPidl),
                _folderPidl = PidlHelper.Concatenate(_folderPidl, pidl),
                _fullPath = childPath,
            };

            if (riid == new Guid(ComGuids.ShellFolder) || riid == new Guid(ComGuids.ShellFolder2))
            {
                ppv = Marshal.GetComInterfaceForObject(folder, typeof(IShellFolder2));
                return HResults.Ok;
            }

            return HResults.NoObject;
        }
        catch
        {
            return HResults.NoObject;
        }
    }

    public int BindToStorage(nint pidl, nint pbc, ref Guid riid, out nint ppv)
    {
        ppv = 0;
        return HResults.NotImpl;
    }

    public int CompareIDs(nint lParam, nint pidl1, nint pidl2) =>
        ShellCompare.CompareRelativePidls(_fullPath, _childCache, lParam, pidl1, pidl2);

    public int CreateViewObject(nint hwndOwner, ref Guid riid, out nint ppv)
    {
        ppv = 0;
        ManagedTrace.Line($"XboxFolder.CreateViewObject enter riid={riid:B} fullPath='{_fullPath}'");
        var shellView = new Guid(ComGuids.ShellView);
        var shellView2 = new Guid(ComGuids.ShellView2);
        if (riid != shellView && riid != shellView2)
        {
            var contextMenu = new Guid(ComGuids.ContextMenu);
            var dataObject = new Guid(ComGuids.DataObject);
            var dropTarget = new Guid(ComGuids.DropTarget);
            if (riid == contextMenu || riid == dataObject || riid == dropTarget)
                return GetUIObjectOf(hwndOwner, 0, 0, ref riid, 0, out ppv);

            return HResults.NotImpl;
        }

        ManagedTrace.Line($"XboxFolder.CreateViewObject IShellView fullPath='{_fullPath}'");
        var callback = new XboxFolderViewCallback(this);
        var hr = NativeMethods.CreateShellFolderView(this, callback, out var view);
        ManagedTrace.Line($"XboxFolder.CreateViewObject CreateShellFolderView hr=0x{hr:X8} view={(view != 0 ? "ok" : "null")}");
        if (hr < 0 || view == 0)
            return hr >= 0 ? HResults.NoObject : hr;

        if (riid == shellView2)
        {
            hr = Marshal.QueryInterface(view, ref riid, out ppv);
            Marshal.Release(view);
            return hr;
        }

        ppv = view;
        return HResults.Ok;
    }

    public int GetAttributesOf(uint cidl, nint apidl, ref uint rgfInOut)
    {
        if (cidl == 0)
        {
            rgfInOut &= FromPath(_fullPath).Attributes;
            return HResults.Ok;
        }

        if (apidl == 0)
            return HResults.InvalidArg;

        uint common = uint.MaxValue;
        for (uint i = 0; i < cidl; i++)
        {
            var pidl = Marshal.ReadIntPtr(apidl, (int)(i * nint.Size));
            common &= GetChildItem(pidl).Attributes;
        }

        rgfInOut &= common;
        return HResults.Ok;
    }

    public int GetUIObjectOf(nint hwndOwner, uint cidl, nint apidl, ref Guid riid, nint prgfInOut, out nint ppv)
    {
        ppv = 0;
        if (_folderPidl == 0)
            return HResults.NoObject;

        var extractIcon = new Guid(ComGuids.ExtractIcon);
        if (riid == extractIcon)
            return NativeMethods.XbShellExt_CreateNativeExtractIcon(_folderPidl, cidl, apidl, ref riid, out ppv);

        var contextMenu = new Guid(ComGuids.ContextMenu);
        if (riid == contextMenu)
            return NativeMethods.XbShellExt_CreateNativeContextMenu(_folderPidl, cidl, apidl, ref riid, out ppv);

        var dataObject = new Guid(ComGuids.DataObject);
        if (riid == dataObject)
        {
            if (cidl == 0)
                return HResults.NoInterface;

            var pidls = ReadChildPidls(cidl, apidl);
            if (pidls.Count == 0 || !SelectionSupportsTransfer(pidls))
                return HResults.NoInterface;

            var obj = new XboxDataObject(_fullPath, pidls);
            ppv = ComObjectExporter.ExportToIUnknown(obj);
            return ppv == 0 ? HResults.NoObject : HResults.Ok;
        }

        var dropTarget = new Guid(ComGuids.DropTarget);
        if (riid == dropTarget)
        {
            if (!ShellSelectionBuilder.SupportsDropTarget(_fullPath, cidl, apidl))
                return HResults.NoInterface;

            var target = new XboxDropTarget(_fullPath, cidl, apidl);
            ppv = ComObjectExporter.ExportToIUnknown(target);
            return ppv == 0 ? HResults.NoObject : HResults.Ok;
        }

        return HResults.NotImpl;
    }

    private static List<nint> ReadChildPidls(uint cidl, nint apidl)
    {
        var pidls = new List<nint>((int)cidl);
        if (cidl == 0 || apidl == 0)
            return pidls;

        for (uint i = 0; i < cidl; i++)
            pidls.Add(Marshal.ReadIntPtr(apidl, (int)(i * nint.Size)));

        return pidls;
    }

    private bool SelectionSupportsTransfer(IReadOnlyList<nint> pidls)
    {
        foreach (var pidl in pidls)
        {
            var item = GetChildItem(pidl);
            if ((item.Attributes & (ShellConstants.SfgaoCancopy | ShellConstants.SfgaoCanmove)) == 0)
                return false;
        }

        return true;
    }

    public int GetDisplayNameOf(nint pidl, uint uFlags, out StrRet pName)
    {
        pName = default;
        try
        {
            var item = GetChildItem(pidl);
            var text = GetItemDisplayText(item, uFlags);
            var buffer = Marshal.StringToCoTaskMemUni(text);
            pName.uType = StrRetType.Wstr;
            pName.u.Pointer = buffer;
            return HResults.Ok;
        }
        catch
        {
            return HResults.NoObject;
        }
    }

    public int SetNameOf(nint hwnd, nint pidl, string pszName, uint uFlags, out nint ppidlOut)
    {
        ppidlOut = 0;
        return HResults.NotImpl;
    }

    public int GetDefaultSearchGUID(out Guid pguid)
    {
        pguid = Guid.Empty;
        return HResults.NotImpl;
    }

    public int EnumSearches(out nint ppenum)
    {
        ppenum = 0;
        return HResults.NotImpl;
    }

    public int GetDefaultColumn(uint dwRes, out uint pSort, out uint pDisplay)
    {
        pSort = 0;
        pDisplay = 0;
        return HResults.Ok;
    }

    public int GetDefaultColumnState(uint iColumn, out uint pcsFlags)
    {
        pcsFlags = 0;
        if (iColumn >= GetColumnCount())
            return HResults.InvalidArg;

        pcsFlags = ShellColumnState.TypeStr | ShellColumnState.OnByDefault;
        return HResults.Ok;
    }

    public int GetDetailsEx(nint pidl, nint pscid, nint pv) => HResults.NotImpl;

    public int GetDetailsOf(nint pidl, uint iColumn, out ShellDetails psd)
    {
        psd = default;
        if (iColumn >= GetColumnCount())
            return HResults.InvalidArg;

        if (pidl == 0)
            return GetColumnHeaderDetails(iColumn, out psd);

        try
        {
            var item = GetChildItem(pidl);
            psd.fmt = 0;
            psd.cxChar = 30;
            psd.str.uType = StrRetType.Wstr;
            psd.str.u.Pointer = Marshal.StringToCoTaskMemUni(GetDetailText(item, iColumn));
            return HResults.Ok;
        }
        catch
        {
            return HResults.NoObject;
        }
    }

    public int MapColumnToSCID(uint iColumn, out ShColumnId pscid)
    {
        pscid = default;
        if (iColumn >= GetColumnCount())
            return HResults.InvalidArg;

        pscid.fmtid = ShellColumnIds.FmtIdStorage;
        pscid.pid = ShellColumnIds.PidStgName;
        return HResults.Ok;
    }

    private uint GetColumnCount()
    {
        if (string.IsNullOrEmpty(_fullPath))
            return 2;

        return FromPath(_fullPath).Kind == XboxItemKind.Console ? 4u : 1u;
    }

    private int GetColumnHeaderDetails(uint iColumn, out ShellDetails psd)
    {
        psd = default;
        if (iColumn >= GetColumnCount())
            return HResults.InvalidArg;

        psd.fmt = 0;
        psd.cxChar = iColumn switch
        {
            2 or 3 => 12,
            1 => 24,
            _ => 30,
        };
        psd.str.uType = StrRetType.Wstr;
        psd.str.u.Pointer = Marshal.StringToCoTaskMemUni(GetColumnHeader(iColumn));
        return HResults.Ok;
    }

    private static string GetColumnHeader(uint iColumn) => iColumn switch
    {
        0 => "Name",
        1 => "Type",
        2 => "Free Space",
        3 => "Total Space",
        _ => "Name",
    };

    private string GetDetailText(XboxShellItem item, uint iColumn)
    {
        if (FromPath(_fullPath).Kind == XboxItemKind.Console && item.Kind == XboxItemKind.Volume)
        {
            return iColumn switch
            {
                0 => item.DisplayName ?? FormattingHelper.GetVolumeDisplayName(FormattingHelper.NormalizeDriveLetter(item.Segment)),
                1 => item.VolumeTypeName ?? FormattingHelper.GetVolumeTypeDescription(FormattingHelper.NormalizeDriveLetter(item.Segment)),
                2 => item.FreeBytes.HasValue ? FormattingHelper.FormatFileSize(item.FreeBytes.Value) : string.Empty,
                3 => item.TotalBytes.HasValue ? FormattingHelper.FormatFileSize(item.TotalBytes.Value) : string.Empty,
                _ => item.Segment,
            };
        }

        return item.DisplayName ?? ShellConstants.SegmentToDisplayName(item.Segment);
    }

    private string GetItemDisplayText(XboxShellItem item, uint uFlags)
    {
        if (item.Kind == XboxItemKind.AddConsole)
            return item.DisplayName ?? ShellConstants.AddConsoleDisplayName;

        if (WantsShortDisplayName(uFlags))
            return GetShortItemDisplayText(item, uFlags);

        if (uFlags == ShellConstants.ShgdnNormal)
            return GetNormalItemDisplayText(item);

        return BuildFullItemDisplayPath(item);
    }

    private static bool WantsShortDisplayName(uint uFlags) =>
        (uFlags & ShellConstants.ShgdnInfolder) != 0 ||
        ((uFlags & ShellConstants.ShgdnForAddressBar) != 0 && (uFlags & ShellConstants.ShgdnForParsing) == 0);

    private string GetShortItemDisplayText(XboxShellItem item, uint uFlags)
    {
        if (item.Kind == XboxItemKind.Volume)
        {
            var letter = FormattingHelper.NormalizeDriveLetter(item.Segment);
            if ((uFlags & ShellConstants.ShgdnForParsing) != 0)
                return $"{letter}:";

            return item.DisplayName ?? FormattingHelper.GetVolumeDisplayName(letter);
        }

        return item.DisplayName ?? ShellConstants.SegmentToDisplayName(item.Segment);
    }

    private string GetNormalItemDisplayText(XboxShellItem item)
    {
        if (item.Kind == XboxItemKind.Volume)
        {
            var letter = FormattingHelper.NormalizeDriveLetter(item.Segment);
            return $"{letter} on {WirePathService.GetConsoleNameFromDisplayPath(_fullPath)}";
        }

        return item.DisplayName ?? ShellConstants.SegmentToDisplayName(item.Segment);
    }

    private string BuildFullItemDisplayPath(XboxShellItem item)
    {
        var relativePath = BuildChildPath(_fullPath, item.Segment);
        return string.IsNullOrEmpty(relativePath)
            ? ShellConstants.NamespaceDisplayName
            : $"{ShellConstants.NamespaceDisplayName}\\{relativePath}";
    }

    private XboxShellItem GetChildItem(nint relativePidl)
    {
        var segment = PidlHelper.GetLastSegment(relativePidl);
        if (_childCache != null && _childCache.TryGetValue(segment, out var cached))
            return cached;

        return XboxShellItemFactory.FromPath(BuildChildPath(_fullPath, segment));
    }

    private bool TryGetBrowsableChild(string segment, out XboxShellItem item)
    {
        item = default!;

        var parent = FromPath(_fullPath);
        if (parent.Kind == XboxItemKind.Console &&
            segment.Length == 1 &&
            char.IsLetter(segment[0]))
        {
            item = FromPath(BuildChildPath(_fullPath, char.ToUpperInvariant(segment[0]).ToString()));
            return item.IsDirectory;
        }

        if (_childCache != null && _childCache.TryGetValue(segment, out var cached))
        {
            item = cached;
            return cached.IsDirectory;
        }

        foreach (var child in XboxShellItemFactory.ListChildren(_fullPath))
        {
            if (!string.Equals(child.Segment, segment, StringComparison.OrdinalIgnoreCase))
                continue;

            item = child;
            return child.IsDirectory;
        }

        return false;
    }

    private string GetChildFullPath(nint relativePidl)
    {
        var segment = PidlHelper.GetLastSegment(relativePidl);
        return BuildChildPath(_fullPath, segment);
    }

    private static string BuildChildPath(string parentPath, string segment) =>
        WirePathService.BuildChildDisplayPath(parentPath, segment);

    private static XboxShellItem FromPath(string path) => XboxShellItemFactory.FromPath(path);

    private static bool MatchesFlags(XboxShellItem item, uint grfFlags)
    {
        var foldersOnly = (grfFlags & ShellConstants.ShcontfFolders) != 0;
        var filesOnly = (grfFlags & ShellConstants.ShcontfNonFolders) != 0;
        if (foldersOnly && !filesOnly && !item.IsDirectory)
            return false;
        if (filesOnly && !foldersOnly && item.IsDirectory)
            return false;
        return true;
    }

    public int ShowPropertiesForSelection(nint hwnd, nint childPidl)
    {
        try
        {
            var selectionPath = childPidl == 0
                ? _fullPath
                : BuildChildPath(_fullPath, PidlHelper.GetLastSegment(childPidl));
            ManagedTrace.Line($"ShowPropertiesForSelection folder='{_fullPath}' selection='{selectionPath}'");
            var request = ShellPropertyRequestBuilder.BuildPropertyRequest(_fullPath, [selectionPath]);
            if (request == null)
            {
                ManagedTrace.Line("ShowPropertiesForSelection request=null");
                ShellUiHost.ShowError(hwnd, "Could not open properties for the current selection.");
                return HResults.NoObject;
            }

            ShellUiHost.ShowProperties(hwnd, request);
            return HResults.Ok;
        }
        catch (Exception ex)
        {
            ManagedTrace.Line($"ShowPropertiesForSelection threw {ex.GetType().Name}: {ex.Message}");
            ShellUiHost.ShowError(hwnd, ex.Message);
            return HResults.NoObject;
        }
    }

    public int ShowSecurityForSelection(nint hwnd)
    {
        if (string.IsNullOrEmpty(_fullPath))
            return HResults.NoObject;

        var request = ShellPropertyRequestBuilder.BuildSecurityRequest(_fullPath);
        ShellUiHost.ShowProperties(hwnd, request, "Security");
        return HResults.Ok;
    }

    public int ShowAddConsoleWizard(nint hwnd)
    {
        ShellUiHost.RunAddConsoleWizard(hwnd);
        return HResults.Ok;
    }

    public int InvokeContextCommand(nint hwnd, nint childPidl, int command)
    {
        try
        {
            ShellCommandService.Execute(hwnd, _fullPath, _folderPidl, childPidl, (ShellContextCommand)command);
            return HResults.Ok;
        }
        catch (Exception ex)
        {
            ManagedTrace.Line($"InvokeContextCommand threw {ex.GetType().Name}: {ex.Message}");
            ShellUiHost.ShowError(hwnd, ex.Message);
            return HResults.NoObject;
        }
    }

    public int GetDragFileGroupDescriptor(uint cidl, nint apidl, out nint phGlobal)
    {
        phGlobal = 0;
        try
        {
            var pidls = ReadChildPidls(cidl, apidl);
            if (pidls.Count == 0)
                return HResults.InvalidArg;

            var selection = ShellSelectionBuilder.BuildFileSelection(_fullPath, pidls);
            if (selection == null)
                return HResults.NoObject;

            // Explorer may request the descriptor more than once during paste. Reuse the
            // live transfer session instead of disposing and opening another XBDM connection.
            if (_dragCatalog != null && _dragTransferSession != null && _dragPidls != null &&
                DragSelectionMatches(_fullPath, _dragPidls, pidls))
            {
                phGlobal = XboxDragFormats.CreateFileGroupDescriptor(_dragCatalog);
                ManagedTrace.Line($"GetDragFileGroupDescriptor reused items={_dragCatalog.Count}");
                return phGlobal == 0 ? HResults.OutOfMemory : HResults.Ok;
            }

            _dragTransferSession?.Dispose();
            ClearDragTransferState();

            (_dragTransferSession, _dragCatalog) = XboxDragTransferSession.Start(selection);
            _dragPidls = pidls;
            phGlobal = XboxDragFormats.CreateFileGroupDescriptor(_dragCatalog);
            ManagedTrace.Line($"GetDragFileGroupDescriptor items={_dragCatalog.Count} firstSize={(_dragCatalog.Count > 0 ? _dragCatalog[0].Size : 0)}");
            return phGlobal == 0 ? HResults.OutOfMemory : HResults.Ok;
        }
        catch (Exception ex)
        {
            ManagedTrace.Line($"GetDragFileGroupDescriptor threw {ex.GetType().Name}: {ex.Message}");
            ClearDragTransferState();
            return HResults.NoObject;
        }
    }

    public int GetDragFileContentsStream(int index, out nint ppStream)
    {
        ppStream = 0;
        try
        {
            if (_dragCatalog == null || _dragPidls == null || _dragTransferSession == null)
                return HResults.NotImpl;

            if (index < 0 || index >= _dragCatalog.Count)
                return HResults.InvalidArg;

            var entry = _dragCatalog[index];
            if (entry.IsDirectory)
                return OleConstants.DvELindex;

            var stream = _dragTransferSession.OpenStream(entry);
            if (stream == null)
                return OleConstants.DvEFormatetc;

            // Must expose the IStream vtable directly. GetIUnknownForObject + native QI
            // fails on the CCW and paste to desktop returns "Unspecified error".
            ppStream = Marshal.GetComInterfaceForObject(stream, typeof(INativeComStream));
            ManagedTrace.Line($"GetDragFileContentsStream index={index} path='{entry.RelativePath}' size={entry.Size}");
            return ppStream == 0 ? HResults.NoObject : HResults.Ok;
        }
        catch (Exception ex)
        {
            ManagedTrace.Line($"GetDragFileContentsStream threw {ex.GetType().Name}: {ex.Message}");
            _dragTransferSession?.ReportFailure(ex.Message);
            ClearDragTransferState();
            return HResults.NoObject;
        }
    }

    public int PerformDrop(nint hwnd, nint childPidl, nint dataObjectUnk, ref uint pdwEffect)
    {
        try
        {
            if (dataObjectUnk == 0)
                return HResults.InvalidArg;

            ManagedTrace.Line($"PerformDrop fullPath='{_fullPath}' childPidl={(childPidl == 0 ? "null" : "set")}");
            var dataObject = (OleIDataObject)Marshal.GetObjectForIUnknown(dataObjectUnk)!;
            var target = new XboxDropTarget(_fullPath, childPidl);
            var pt = default(POINTL);
            return target.Drop(dataObject, 0, pt, ref pdwEffect);
        }
        catch (Exception ex)
        {
            ManagedTrace.Line($"PerformDrop threw {ex.GetType().Name}: {ex.Message}");
            ShellUiHost.ShowError(hwnd, ex.Message);
            return HResults.NoObject;
        }
    }

    private void ClearDragTransferState()
    {
        _dragTransferSession = null;
        _dragCatalog = null;
        _dragPidls = null;
    }

    private static bool DragSelectionMatches(string folderPath, IReadOnlyList<nint> previous, IReadOnlyList<nint> current)
    {
        if (previous.Count != current.Count)
            return false;

        for (var i = 0; i < previous.Count; i++)
        {
            if (!string.Equals(
                    PidlHelper.GetLastSegment(previous[i]),
                    PidlHelper.GetLastSegment(current[i]),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
