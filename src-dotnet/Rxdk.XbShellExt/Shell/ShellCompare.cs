using Rxdk.XbShellExt.Interop;
using Rxdk.Xbdm.KitServices.Services;

namespace Rxdk.XbShellExt.Shell;

internal static class ShellCompare
{
    public static int CompareRelativePidls(
        string folderPath,
        Dictionary<string, XboxShellItem>? childCache,
        nint lParam,
        nint pidl1,
        nint pidl2)
    {
        var offset1 = 0;
        var offset2 = 0;
        var cbTotal = 0;

        while (true)
        {
            var cb1 = PidlHelper.ReadItemSize(pidl1, offset1);
            var cb2 = PidlHelper.ReadItemSize(pidl2, offset2);
            if (cb1 == 0 && cb2 == 0)
                return HResults.Equal;
            if (cb1 == 0)
                return HResults.Greater;
            if (cb2 == 0)
                return HResults.Less;

            var seg1 = PidlHelper.ReadItemSegment(pidl1, offset1, cb1);
            var seg2 = PidlHelper.ReadItemSegment(pidl2, offset2, cb2);
            var nameCmp = string.Compare(seg1, seg2, StringComparison.OrdinalIgnoreCase);
            if (nameCmp != 0)
            {
                var next1 = PidlHelper.ReadItemSize(pidl1, offset1 + cb1);
                var next2 = PidlHelper.ReadItemSize(pidl2, offset2 + cb2);
                if (next1 != 0 || next2 != 0 || cbTotal != 0)
                    return ResultFromNameCompare(nameCmp);

                var column = (uint)(lParam & ShellConstants.ShcidsColumnMask);
                return CompareImmediateChildren(folderPath, childCache, column, seg1, seg2, nameCmp);
            }

            cbTotal += cb1;
            offset1 += cb1;
            offset2 += cb2;
        }
    }

    private static int CompareImmediateChildren(
        string folderPath,
        Dictionary<string, XboxShellItem>? childCache,
        uint column,
        string segment1,
        string segment2,
        int nameCmp)
    {
        var item1 = ResolveChild(folderPath, childCache, segment1);
        var item2 = ResolveChild(folderPath, childCache, segment2);
        var folderKind = XboxShellItemFactory.FromPath(folderPath).Kind;

        if (folderKind == XboxItemKind.Console && item1.Kind == XboxItemKind.Volume && item2.Kind == XboxItemKind.Volume)
        {
            var cmp = column switch
            {
                1 => CompareVolumeType(item1, item2),
                2 => CompareNullableULong(item1.FreeBytes, item2.FreeBytes),
                3 => CompareNullableULong(item1.TotalBytes, item2.TotalBytes),
                _ => nameCmp,
            };

            if (cmp != 0)
                return ResultFromNameCompare(cmp);
        }

        return ResultFromNameCompare(nameCmp);
    }

    private static XboxShellItem ResolveChild(
        string folderPath,
        Dictionary<string, XboxShellItem>? childCache,
        string segment)
    {
        if (childCache != null && childCache.TryGetValue(segment, out var cached))
            return cached;

        var childPath = string.IsNullOrEmpty(folderPath)
            ? segment
            : WirePathService.GetItemDisplayPath(folderPath, segment);
        return XboxShellItemFactory.FromPath(childPath);
    }

    private static int CompareVolumeType(XboxShellItem left, XboxShellItem right)
    {
        var leftKey = FormattingHelper.GetVolumeTypeSortKey(FormattingHelper.NormalizeDriveLetter(left.Segment));
        var rightKey = FormattingHelper.GetVolumeTypeSortKey(FormattingHelper.NormalizeDriveLetter(right.Segment));
        return leftKey.CompareTo(rightKey);
    }

    private static int CompareNullableULong(ulong? left, ulong? right)
    {
        var l = left ?? 0;
        var r = right ?? 0;
        return l.CompareTo(r);
    }

    private static int ResultFromNameCompare(int nameCmp)
    {
        if (nameCmp < 0)
            return HResults.Less;
        if (nameCmp > 0)
            return HResults.Greater;
        return HResults.Equal;
    }
}
