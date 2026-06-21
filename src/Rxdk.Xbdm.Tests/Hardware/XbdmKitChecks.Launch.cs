using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Tests.Hardware;

internal static partial class XbdmKitChecks
{
    private static KitCheckResult CompareUploadTriangleXbe(XbdmKitSession session)
    {
        var localXbe = Path.Combine(XbdmKitSession.TestFilesDirectory(), "TriangleXDK.xbe");
        if (!File.Exists(localXbe))
        {
            return KitCheck.Skip(
                LaunchCategory,
                "UploadTriangleXbe",
                $"Missing {localXbe}");
        }

        var localSize = new FileInfo(localXbe).Length;
        var wire = XbdmKitSession.NewScratchWirePath(session.Managed, "TriangleXDK.xbe");
        var dir = wire[..wire.LastIndexOf('\\')];

        try
        {
            XbdmKitSession.EnsureScratchDirectory(session.Managed, wire);
            session.Managed.SendFile(localXbe, wire);

            var attr = session.Managed.GetFileAttributes(wire);
            return attr.Size == (ulong)localSize
                ? KitCheck.Pass(LaunchCategory, "UploadTriangleXbe", $"{localSize} bytes")
                : KitCheck.Fail(
                    LaunchCategory,
                    "UploadTriangleXbe",
                    "Uploaded XBE size mismatch.",
                    attr.Size.ToString());
        }
        finally
        {
            TryDelete(session.Managed, wire, dir);
        }
    }

    private static KitCheckResult CompareTriangleXbeInfo(XbdmKitSession session)
    {
        var localXbe = Path.Combine(XbdmKitSession.TestFilesDirectory(), "TriangleXDK.xbe");
        if (!File.Exists(localXbe))
            return KitCheck.Skip(LaunchCategory, "GetXbeInfo(TriangleXDK)", "Test file missing.");

        var wire = XbdmKitSession.NewScratchWirePath(session.Managed, "TriangleXDK.xbe");
        XbdmKitSession.EnsureTriangleXbeOnKit(session.Managed, localXbe, wire);

        try
        {
            var info = session.Managed.GetXbeInfo(wire);
            if (!WirePathsEqual(info.LaunchPath, wire))
            {
                return KitCheck.Fail(
                    LaunchCategory,
                    "GetXbeInfo(TriangleXDK)",
                    "LaunchPath does not match uploaded XBE.",
                    info.LaunchPath);
            }

            return KitCheck.Pass(
                LaunchCategory,
                "GetXbeInfo(TriangleXDK)",
                $"ts=0x{info.TimeStamp:x} sum=0x{info.CheckSum:x} stack=0x{info.StackSize:x}");
        }
        catch (XbdmException ex)
        {
            return KitCheck.Fail(LaunchCategory, "GetXbeInfo(TriangleXDK)", ex.Message);
        }
    }

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
