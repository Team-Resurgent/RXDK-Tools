using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Tests.Parity;

internal static partial class XbdmParityChecks
{
    private static ParityCheckResult CompareUploadTriangleXbe(XbdmParitySession session)
    {
        var localXbe = Path.Combine(XbdmParitySession.TestFilesDirectory(), "TriangleXDK.xbe");
        if (!File.Exists(localXbe))
        {
            return ParityCompare.Skip(
                LaunchCategory,
                "UploadTriangleXbe",
                $"Missing {localXbe}");
        }

        var localSize = new FileInfo(localXbe).Length;
        var nativeWire = XbdmParitySession.NewScratchWirePath(session.Native, "TriangleXDK.xbe");
        var managedWire = XbdmParitySession.NewScratchWirePath(session.Managed, "TriangleXDK.xbe");
        var nativeDir = nativeWire[..nativeWire.LastIndexOf('\\')];
        var managedDir = managedWire[..managedWire.LastIndexOf('\\')];

        try
        {
            XbdmParitySession.EnsureScratchDirectory(session.Native, nativeWire);
            XbdmParitySession.EnsureScratchDirectory(session.Managed, managedWire);
            session.Native.SendFile(localXbe, nativeWire);
            session.Managed.SendFile(localXbe, managedWire);

            var nativeAttr = session.Native.GetFileAttributes(nativeWire);
            var managedAttr = session.Managed.GetFileAttributes(managedWire);
            if (nativeAttr.Size == (ulong)localSize &&
                managedAttr.Size == (ulong)localSize &&
                nativeAttr.Size == managedAttr.Size)
            {
                return ParityCompare.Pass(LaunchCategory, "UploadTriangleXbe", $"{localSize} bytes");
            }

            return ParityCompare.Fail(
                LaunchCategory,
                "UploadTriangleXbe",
                "Uploaded XBE size mismatch.",
                nativeAttr.Size.ToString(),
                managedAttr.Size.ToString());
        }
        finally
        {
            TryDelete(session.Native, nativeWire, nativeDir);
            TryDelete(session.Managed, managedWire, managedDir);
        }
    }

    private static ParityCheckResult CompareTriangleXbeInfo(XbdmParitySession session)
    {
        var localXbe = Path.Combine(XbdmParitySession.TestFilesDirectory(), "TriangleXDK.xbe");
        if (!File.Exists(localXbe))
            return ParityCompare.Skip(LaunchCategory, "GetXbeInfo(TriangleXDK)", "Test file missing.");

        var nativeWire = XbdmParitySession.NewScratchWirePath(session.Native, "TriangleXDK.xbe");
        var managedWire = XbdmParitySession.NewScratchWirePath(session.Managed, "TriangleXDK.xbe");
        XbdmParitySession.EnsureTriangleXbeOnKit(session.Native, localXbe, nativeWire);
        XbdmParitySession.EnsureTriangleXbeOnKit(session.Managed, localXbe, managedWire);

        XbdmXbeInfo nativeInfo;
        XbdmXbeInfo managedInfo;
        try
        {
            nativeInfo = session.Native.GetXbeInfo(nativeWire);
            managedInfo = session.Managed.GetXbeInfo(managedWire);
        }
        catch (XbdmException ex)
        {
            return ParityCompare.Fail(LaunchCategory, "GetXbeInfo(TriangleXDK)", ex.Message);
        }

        if (!WirePathsEqual(nativeInfo.LaunchPath, nativeWire))
        {
            return ParityCompare.Fail(
                LaunchCategory,
                "GetXbeInfo(TriangleXDK)",
                "Native LaunchPath does not match uploaded XBE.",
                nativeInfo.LaunchPath,
                nativeWire);
        }

        if (!WirePathsEqual(managedInfo.LaunchPath, managedWire))
        {
            return ParityCompare.Fail(
                LaunchCategory,
                "GetXbeInfo(TriangleXDK)",
                "Managed LaunchPath does not match uploaded XBE.",
                managedInfo.LaunchPath,
                managedWire);
        }

        return ParityCompare.Equal(
            LaunchCategory,
            "GetXbeInfo(TriangleXDK)",
            XbeInfoMetadata(nativeInfo),
            XbeInfoMetadata(managedInfo),
            $"ts=0x{nativeInfo.TimeStamp:x} sum=0x{nativeInfo.CheckSum:x} stack=0x{nativeInfo.StackSize:x}");
    }

    private static (uint TimeStamp, uint CheckSum, uint StackSize) XbeInfoMetadata(XbdmXbeInfo info) =>
        (info.TimeStamp, info.CheckSum, info.StackSize);

    private static bool WirePathsEqual(string left, string right) =>
        string.Equals(NormalizeWirePath(left), NormalizeWirePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeWirePath(string path) =>
        path.Replace('/', '\\').TrimEnd('\\');

    private static void TryDelete(IXbdmConnection connection, string fileWire, string dirWire)
    {
        try
        {
            connection.Delete(fileWire, isDirectory: false);
        }
        catch (XbdmException)
        {
        }

        try
        {
            connection.Delete(dirWire, isDirectory: true);
        }
        catch (XbdmException)
        {
        }
    }
}
