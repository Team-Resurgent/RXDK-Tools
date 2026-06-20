using System.Runtime.InteropServices;
using Rxdk.XbShellExt.Interop;
using Rxdk.XbShellExt.Shell;

namespace Rxdk.XbShellExt.Com;

[ComVisible(true)]
[Guid(ComGuids.EnumIdList)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class XboxEnumIdList : IEnumIDList
{
    private readonly IReadOnlyList<nint> _pidls;
    private int _index;

    public XboxEnumIdList(IReadOnlyList<nint> pidls) => _pidls = pidls;

    public int Next(uint celt, nint rgelt, out uint pceltFetched)
    {
        pceltFetched = 0;
        if (rgelt == 0 || _index >= _pidls.Count)
            return HResults.False;

        uint fetched = 0;
        for (uint i = 0; i < celt && _index < _pidls.Count; i++)
        {
            var clone = PidlHelper.Clone(_pidls[_index++]);
            Marshal.WriteIntPtr(rgelt, (int)(i * nint.Size), clone);
            fetched++;
        }

        pceltFetched = fetched;
        return fetched == 0 ? HResults.False : HResults.Ok;
    }

    public int Skip(uint celt)
    {
        _index += (int)celt;
        return HResults.Ok;
    }

    public int Reset()
    {
        _index = 0;
        return HResults.Ok;
    }

    public int Clone(out IEnumIDList? ppEnum)
    {
        ppEnum = new XboxEnumIdList(_pidls) { _index = _index };
        return HResults.Ok;
    }
}
