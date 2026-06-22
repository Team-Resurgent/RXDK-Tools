using System.Runtime.InteropServices.ComTypes;
using Rxdk.XbShellExt.Interop;
using Rxdk.XbShellExt.Shell;
using Rxdk.Xbdm.KitServices.Models;
using Rxdk.Xbdm.KitServices.Services;
using Rxdk.Xbdm.Managed;
using OleIDataObject = Rxdk.XbShellExt.Interop.IDataObject;

namespace Rxdk.XbShellExt.Com;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class XboxDataObject : OleIDataObject, IAsyncOperation
{
    private readonly FileSelection? _selection;
    private readonly nint _threadRef;
    private IReadOnlyList<XboxDragEntry>? _catalog;
    private XboxDragTransferSession? _transferSession;
    private uint _preferredEffect = OleConstants.DropeffectCopy;
    private bool _asyncStarted;

    public XboxDataObject(string folderPath, IReadOnlyList<nint> childPidls)
    {
        _selection = ShellSelectionBuilder.BuildFileSelection(folderPath, childPidls);
        if (OleDataTransfer.SHGetThreadRef(out _threadRef) >= 0 && _threadRef != 0)
            Marshal.AddRef(_threadRef);
    }

    ~XboxDataObject()
    {
        if (_threadRef != 0)
            Marshal.Release(_threadRef);
    }

    public int GetData(ref FORMATETC pFormatetc, out STGMEDIUM pMedium)
    {
        pMedium = default;
        if (_selection == null || _selection.Items.Count == 0)
            return OleConstants.DvEFormatetc;

        if (pFormatetc.cfFormat == ClipboardFormats.FileDescriptorW)
        {
            try
            {
                var hGlobal = XboxDragFormats.CreateFileGroupDescriptor(EnsureCatalog());
                if (hGlobal == IntPtr.Zero)
                    return HResults.OutOfMemory;

                pMedium = CreateGlobalMedium(hGlobal);
                return HResults.Ok;
            }
            catch
            {
                return OleConstants.DvEFormatetc;
            }
        }

        if (pFormatetc.cfFormat == ClipboardFormats.FileContents)
        {
            try
            {
                var catalog = EnsureCatalog();
                if (pFormatetc.lindex < 0 || pFormatetc.lindex >= catalog.Count)
                    return OleConstants.DvELindex;

                if (catalog[pFormatetc.lindex].IsDirectory)
                    return OleConstants.DvELindex;

                if (!TryGetFileEntry(pFormatetc.lindex, out var entry))
                    return OleConstants.DvEFormatetc;

                var stream = XboxDragFormats.OpenFileContentsStream(
                    entry,
                    EnsureTransferSession());
                if (stream == null)
                    return OleConstants.DvEFormatetc;

                var streamPtr = Marshal.GetComInterfaceForObject(stream, typeof(INativeComStream));
                pMedium = new STGMEDIUM
                {
                    tymed = (TYMED)OleConstants.TymedIstream,
                    unionmember = streamPtr,
                    pUnkForRelease = IntPtr.Zero,
                };
                return HResults.Ok;
            }
            catch (Exception ex)
            {
                _transferSession?.ReportFailure(ex.Message);
                return OleConstants.DvEFormatetc;
            }
        }

        if (pFormatetc.cfFormat == ClipboardFormats.XboxFileDescriptor)
        {
            var hGlobal = XboxClipboardPayload.Serialize(_selection, FileClipboardOperation.Copy);
            if (hGlobal == IntPtr.Zero)
                return HResults.OutOfMemory;

            pMedium = CreateGlobalMedium(hGlobal);
            return HResults.Ok;
        }

        if (pFormatetc.cfFormat == ClipboardFormats.PreferredDropEffect)
        {
            var hGlobal = OleDataTransfer.CreateDropEffect(_preferredEffect);
            if (hGlobal == IntPtr.Zero)
                return HResults.OutOfMemory;

            pMedium = CreateGlobalMedium(hGlobal);
            return HResults.Ok;
        }

        return OleConstants.DvEFormatetc;
    }

    public int GetDataHere(ref FORMATETC pFormatetc, ref STGMEDIUM pMedium) => OleConstants.DvEFormatetc;

    public int QueryGetData(ref FORMATETC pFormatetc)
    {
        if (_selection == null || _selection.Items.Count == 0)
            return OleConstants.DvEFormatetc;

        if (pFormatetc.cfFormat == ClipboardFormats.FileDescriptorW ||
            pFormatetc.cfFormat == ClipboardFormats.XboxFileDescriptor ||
            pFormatetc.cfFormat == ClipboardFormats.PreferredDropEffect)
            return HResults.Ok;

        if (pFormatetc.cfFormat == ClipboardFormats.FileContents)
        {
            if (pFormatetc.lindex < 0)
                return HasAnyFileEntry() ? HResults.Ok : OleConstants.DvEFormatetc;

            var catalog = EnsureCatalog();
            if (pFormatetc.lindex >= catalog.Count)
                return OleConstants.DvELindex;

            if (catalog[pFormatetc.lindex].IsDirectory)
                return OleConstants.DvELindex;

            return HResults.Ok;
        }

        return OleConstants.DvEFormatetc;
    }

    public int GetCanonicalFormatEtc(ref FORMATETC pFormatetcIn, out FORMATETC pFormatetcOut)
    {
        pFormatetcOut = default;
        return HResults.NotImpl;
    }

    public int SetData(ref FORMATETC pFormatetc, ref STGMEDIUM pMedium, bool fRelease)
    {
        if (pFormatetc.cfFormat == ClipboardFormats.PreferredDropEffect && pMedium.unionmember != IntPtr.Zero)
        {
            var locked = GlobalLock(pMedium.unionmember);
            try
            {
                _preferredEffect = (uint)Marshal.ReadInt32(locked);
            }
            finally
            {
                GlobalUnlock(pMedium.unionmember);
            }

            return HResults.Ok;
        }

        return HResults.NotImpl;
    }

    public int EnumFormatEtc(uint dwDirection, out IEnumFORMATETC? ppenumFormatEtc)
    {
        ppenumFormatEtc = null;
        if (_selection == null || _selection.Items.Count == 0)
            return HResults.NotImpl;

        if (dwDirection == OleConstants.DatadirGet)
        {
            ppenumFormatEtc = new FormatEnumerator(
                OleDataTransfer.CreateFormat(ClipboardFormats.FileDescriptorW),
                OleDataTransfer.CreateFormat(ClipboardFormats.FileContents, OleConstants.TymedIstream),
                OleDataTransfer.CreateFormat(ClipboardFormats.XboxFileDescriptor),
                OleDataTransfer.CreateFormat(ClipboardFormats.PreferredDropEffect));
            return HResults.Ok;
        }

        if (dwDirection == OleConstants.DatadirSet)
        {
            ppenumFormatEtc = new FormatEnumerator(
                OleDataTransfer.CreateFormat(ClipboardFormats.PreferredDropEffect));
            return HResults.Ok;
        }

        return HResults.NotImpl;
    }

    public int DAdvise(ref FORMATETC pFormatetc, uint advf, IAdviseSink pAdvSink, out uint pdwConnection)
    {
        pdwConnection = 0;
        return unchecked((int)0x80040001);
    }

    public int DUnadvise(uint dwConnection) => unchecked((int)0x80040001);

    public int EnumDAdvise(out IEnumSTATDATA? ppenumAdvise)
    {
        ppenumAdvise = null;
        return unchecked((int)0x80040001);
    }

    public int SetAsyncMode(bool fDoOpAsync) => HResults.Ok;

    public int GetAsyncMode(out bool pfIsOpAsync)
    {
        pfIsOpAsync = false;
        return HResults.Ok;
    }

    public int StartOperation(nint pbcReserved)
    {
        _asyncStarted = true;
        return HResults.Ok;
    }

    public int InOperation(out bool pfInAsyncOp)
    {
        pfInAsyncOp = _asyncStarted;
        return HResults.Ok;
    }

    public int EndOperation(int hResult, nint pbcReserved, uint dwEffects)
    {
        _asyncStarted = false;
        try
        {
            if (hResult >= 0 && dwEffects == OleConstants.DropeffectMove && _selection != null)
                new FileOperationsService(new FileClipboardService()).DeleteSelectionWithoutPrompt(_selection);
        }
        catch
        {
        }
        finally
        {
            _transferSession?.NotifyOwnerReleased();
            _transferSession = null;
        }

        return HResults.Ok;
    }

    private IReadOnlyList<XboxDragEntry> EnsureCatalog()
    {
        if (_catalog != null)
            return _catalog;

        EnsureTransferSession();
        return _catalog!;
    }

    private XboxDragTransferSession EnsureTransferSession()
    {
        if (_transferSession != null)
            return _transferSession;

        var (session, catalog) = XboxDragTransferSession.Start(_selection!);
        _catalog = catalog;
        return _transferSession = session;
    }

    private bool HasAnyFileEntry() => EnsureCatalog().Any(entry => !entry.IsDirectory);

    private bool TryGetFileEntry(int index, out XboxDragEntry entry)
    {
        entry = null!;
        if (index < 0)
            return false;

        var catalog = EnsureCatalog();
        if (index >= catalog.Count)
            return false;

        entry = catalog[index];
        return !entry.IsDirectory;
    }

    private static STGMEDIUM CreateGlobalMedium(nint hGlobal) =>
        new()
        {
            tymed = (TYMED)OleConstants.TymedHglobal,
            unionmember = hGlobal,
            pUnkForRelease = IntPtr.Zero,
        };

    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint hMem);

    private sealed class FormatEnumerator : IEnumFORMATETC
    {
        private readonly IReadOnlyList<FORMATETC> _formats;
        private int _index;

        public FormatEnumerator(params FORMATETC[] formats) => _formats = formats;

        public void Clone(out IEnumFORMATETC ppenum)
        {
            var clone = new FormatEnumerator(_formats.ToArray());
            clone._index = _index;
            ppenum = clone;
        }

        public int Next(int celt, FORMATETC[] rgelt, int[]? pceltFetched)
        {
            var fetched = 0;
            while (fetched < celt && _index < _formats.Count)
            {
                rgelt[fetched] = _formats[_index++];
                fetched++;
            }

            if (pceltFetched is { Length: > 0 })
                pceltFetched[0] = fetched;

            return fetched == 0 ? 1 : HResults.Ok;
        }

        public int Reset()
        {
            _index = 0;
            return HResults.Ok;
        }

        public int Skip(int celt)
        {
            _index = Math.Min(_index + celt, _formats.Count);
            return _index >= _formats.Count ? 1 : HResults.Ok;
        }
    }
}
