using System.Text;
using Rxdk.XbShellExt.Interop;

namespace Rxdk.XbShellExt.Shell;

internal static class PidlHelper
{
    public static nint CreateEmpty()
    {
        var pidl = NativeMethods.CoTaskMemAlloc(2);
        if (pidl == 0)
            throw new OutOfMemoryException();

        Marshal.WriteInt16(pidl, 0);
        return pidl;
    }

    public static nint CreateSimple(string segment)
    {
        var bytes = Encoding.ASCII.GetBytes(segment + '\0');
        var cb = (ushort)(Marshal.SizeOf<ushort>() + bytes.Length);
        var total = cb + sizeof(ushort);
        var pidl = NativeMethods.CoTaskMemAlloc((nuint)total);
        if (pidl == 0)
            throw new OutOfMemoryException();

        Marshal.WriteInt16(pidl, (short)cb);
        Marshal.Copy(bytes, 0, pidl + sizeof(ushort), bytes.Length);
        Marshal.WriteInt16(pidl + cb, 0);
        return pidl;
    }

    public static nint Concatenate(nint absolute, nint relativeSimple)
    {
        var combined = NativeMethods.ILCombine(absolute, relativeSimple);
        if (combined == 0)
            throw new OutOfMemoryException();

        return combined;
    }

    public static nint GetRelativeSuffix(nint rootPidl, nint absolutePidl)
    {
        if (rootPidl == 0 || absolutePidl == 0)
            return Clone(absolutePidl);

        var rootLen = GetLength(rootPidl);
        if (rootLen <= 2)
            return Clone(absolutePidl);

        // Root pidl includes its terminator; relative portion starts after root bytes minus terminator.
        var suffixStart = rootLen - sizeof(ushort);
        var suffixLen = GetLength(absolutePidl) - suffixStart;
        if (suffixLen <= 2)
            return CreateEmpty();

        var relative = NativeMethods.CoTaskMemAlloc((nuint)suffixLen);
        if (relative == 0)
            throw new OutOfMemoryException();

        CopyMemory(absolutePidl + suffixStart, suffixLen, relative);
        return relative;
    }

    public static nint Clone(nint pidl)
    {
        if (pidl == 0)
            return CreateEmpty();

        var clone = NativeMethods.ILClone(pidl);
        return clone != 0 ? clone : CreateEmpty();
    }

    public static void Free(nint pidl)
    {
        if (pidl == 0)
            return;

        NativeMethods.CoTaskMemFree(pidl);
    }

    public static int GetLength(nint pidl)
    {
        if (pidl == 0)
            return 2;

        var length = 0;
        while (true)
        {
            var cb = ReadUInt16(pidl + length);
            if (cb == 0)
                break;
            length += cb;
        }

        return length + sizeof(ushort);
    }

    public static string GetLastSegment(nint pidl)
    {
        if (pidl == 0)
            return string.Empty;

        var offset = 0;
        var last = string.Empty;
        while (true)
        {
            var cb = ReadUInt16(pidl + offset);
            if (cb == 0)
                break;

            last = ReadSegment(pidl + offset + sizeof(ushort), cb - sizeof(ushort));
            offset += cb;
        }

        return last;
    }

    public static IReadOnlyList<string> GetSegments(nint pidl)
    {
        var segments = new List<string>();
        if (pidl == 0)
            return segments;

        var offset = 0;
        while (true)
        {
            var cb = ReadUInt16(pidl + offset);
            if (cb == 0)
                break;

            segments.Add(ReadSegment(pidl + offset + sizeof(ushort), cb - sizeof(ushort)));
            offset += cb;
        }

        return segments;
    }

    public static string BuildPath(nint pidl) =>
        string.Join('\\', GetSegments(pidl));

    // IPersistFolder::Initialize receives the fully qualified pidl, whose
    // leading itemids are opaque shell IDs (the desktop/CLSID prefix) and whose
    // trailing itemids are our own printable-ASCII simple pidls. Return the
    // namespace-relative path by keeping only the trailing run of ASCII
    // segments (e.g. "myxbox" or "myxbox\\C:"); the root yields "".
    public static string GetNamespaceRelativePath(nint pidl)
    {
        var segments = new List<string>();
        if (pidl == 0)
            return string.Empty;

        var offset = 0;
        while (true)
        {
            var cb = ReadUInt16(pidl + offset);
            if (cb == 0)
                break;

            var segment = TryReadAsciiSegment(pidl + offset + sizeof(ushort), cb - sizeof(ushort));
            if (segment == null)
                segments.Clear();
            else
                segments.Add(segment);

            offset += cb;
        }

        return string.Join('\\', segments);
    }

    public static nint BuildNamespaceRelativePidl(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            return CreateEmpty();

        nint combined = 0;
        foreach (var segment in fullPath.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var simple = CreateSimple(segment);
            if (combined == 0)
            {
                combined = simple;
                continue;
            }

            var next = Concatenate(combined, simple);
            Free(combined);
            combined = next;
        }

        return combined;
    }

    public static int ReadItemSize(nint pidl, int offset) => ReadUInt16(pidl + offset);

    public static string ReadItemSegment(nint pidl, int offset, int cb) =>
        ReadSegment(pidl + offset + sizeof(ushort), cb - sizeof(ushort));

    public static int Compare(nint pidl1, nint pidl2)
    {
        var offset1 = 0;
        var offset2 = 0;
        while (true)
        {
            var cb1 = ReadUInt16(pidl1 + offset1);
            var cb2 = ReadUInt16(pidl2 + offset2);
            if (cb1 == 0 && cb2 == 0)
                return HResults.Equal;
            if (cb1 == 0)
                return HResults.Less;
            if (cb2 == 0)
                return HResults.Greater;

            var seg1 = ReadSegment(pidl1 + offset1 + sizeof(ushort), cb1 - sizeof(ushort));
            var seg2 = ReadSegment(pidl2 + offset2 + sizeof(ushort), cb2 - sizeof(ushort));
            var cmp = string.Compare(seg1, seg2, StringComparison.OrdinalIgnoreCase);
            if (cmp != 0)
                return cmp < 0 ? HResults.Less : HResults.Greater;

            offset1 += cb1;
            offset2 += cb2;
        }
    }

    private static string ReadSegment(nint address, int maxBytes)
    {
        var bytes = new byte[maxBytes];
        Marshal.Copy(address, bytes, 0, maxBytes);
        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0)
            length = maxBytes;
        return Encoding.ASCII.GetString(bytes, 0, length);
    }

    // Returns the segment text when every byte up to the first null is printable
    // ASCII (one of our simple pidls); returns null for opaque/binary itemids.
    private static string? TryReadAsciiSegment(nint address, int maxBytes)
    {
        if (maxBytes <= 0)
            return null;

        var bytes = new byte[maxBytes];
        Marshal.Copy(address, bytes, 0, maxBytes);

        var length = 0;
        while (length < maxBytes && bytes[length] != 0)
        {
            var b = bytes[length];
            if (b < 0x20 || b > 0x7E)
                return null;
            length++;
        }

        return length == 0 ? null : Encoding.ASCII.GetString(bytes, 0, length);
    }

    private static ushort ReadUInt16(nint address) => (ushort)Marshal.ReadInt16(address);

    private static void CopyMemory(nint source, int length, nint destination)
    {
        var buffer = new byte[length];
        Marshal.Copy(source, buffer, 0, length);
        Marshal.Copy(buffer, 0, destination, length);
    }
}
