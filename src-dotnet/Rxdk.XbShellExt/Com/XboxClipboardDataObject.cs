using System.Runtime.InteropServices.ComTypes;

using Rxdk.XbShellExt.Interop;

using Rxdk.XbShellExt.Shell;

using Rxdk.Xbdm.KitServices.Models;

using OleIDataObject = Rxdk.XbShellExt.Interop.IDataObject;



namespace Rxdk.XbShellExt.Com;



[ComVisible(true)]

[ClassInterface(ClassInterfaceType.None)]

internal sealed class XboxClipboardDataObject : OleIDataObject

{

    private readonly FileSelection _selection;

    private readonly FileClipboardOperation _operation;

    private readonly uint _preferredEffect;



    public XboxClipboardDataObject(FileSelection selection, FileClipboardOperation operation, uint preferredEffect)

    {

        _selection = selection;

        _operation = operation;

        _preferredEffect = preferredEffect;

    }



    public int GetData(ref FORMATETC pFormatetc, out STGMEDIUM pMedium)

    {

        pMedium = default;

        if (pFormatetc.cfFormat == ClipboardFormats.PreferredDropEffect)

        {

            var hGlobal = OleDataTransfer.CreateDropEffect(_preferredEffect);

            if (hGlobal == IntPtr.Zero)

                return HResults.OutOfMemory;



            pMedium = CreateGlobalMedium(hGlobal);

            return HResults.Ok;

        }



        if (pFormatetc.cfFormat == ClipboardFormats.XboxFileDescriptor)

        {

            var hGlobal = XboxClipboardPayload.Serialize(_selection, _operation);

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

        if (pFormatetc.cfFormat == ClipboardFormats.PreferredDropEffect ||

            pFormatetc.cfFormat == ClipboardFormats.XboxFileDescriptor)

            return HResults.Ok;



        return OleConstants.DvEFormatetc;

    }



    public int GetCanonicalFormatEtc(ref FORMATETC pFormatetcIn, out FORMATETC pFormatetcOut)

    {

        pFormatetcOut = default;

        return HResults.NotImpl;

    }



    public int SetData(ref FORMATETC pFormatetc, ref STGMEDIUM pMedium, bool fRelease) => HResults.NotImpl;



    public int EnumFormatEtc(uint dwDirection, out IEnumFORMATETC? ppenumFormatEtc)

    {

        ppenumFormatEtc = null;

        if (dwDirection != OleConstants.DatadirGet)

            return HResults.NotImpl;



        ppenumFormatEtc = new ClipboardFormatEnumerator(

            OleDataTransfer.CreateFormat(ClipboardFormats.PreferredDropEffect),

            OleDataTransfer.CreateFormat(ClipboardFormats.XboxFileDescriptor));

        return HResults.Ok;

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



    private static STGMEDIUM CreateGlobalMedium(nint hGlobal) =>

        new()

        {

            tymed = (TYMED)OleConstants.TymedHglobal,

            unionmember = hGlobal,

            pUnkForRelease = IntPtr.Zero,

        };



    private sealed class ClipboardFormatEnumerator : IEnumFORMATETC

    {

        private readonly FORMATETC[] _formats;

        private int _index;



        public ClipboardFormatEnumerator(params FORMATETC[] formats) => _formats = formats;



        public void Clone(out IEnumFORMATETC ppenum)
        {
            var clone = new ClipboardFormatEnumerator(_formats.ToArray());
            clone._index = _index;
            ppenum = clone;
        }



        public int Next(int celt, FORMATETC[] rgelt, int[]? pceltFetched)

        {

            var fetched = 0;

            while (fetched < celt && _index < _formats.Length)

                rgelt[fetched++] = _formats[_index++];



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

            _index = Math.Min(_index + celt, _formats.Length);

            return _index >= _formats.Length ? 1 : HResults.Ok;

        }

    }

}


