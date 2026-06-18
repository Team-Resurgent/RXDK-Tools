using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Tests.Parity;

internal static partial class XbdmParityChecks
{
    private static IEnumerable<ParityCheckResult> ExecuteFileRoundTrip(XbdmParitySession session)
    {
        var localPayload = Path.Combine(Path.GetTempPath(), $"xbdm-parity-{Guid.NewGuid():N}.bin");
        var nativeReceive = Path.Combine(Path.GetTempPath(), $"xbdm-native-rx-{Guid.NewGuid():N}.bin");
        var managedReceive = Path.Combine(Path.GetTempPath(), $"xbdm-managed-rx-{Guid.NewGuid():N}.bin");
        var payload = Enumerable.Range(0, 4096).Select(i => (byte)(i & 0xFF)).ToArray();
        File.WriteAllBytes(localPayload, payload);

        var nativeWireFile = XbdmParitySession.NewScratchWirePath(session.Native, "payload.bin");
        var nativeWireDir = nativeWireFile[..nativeWireFile.LastIndexOf('\\')];

        var managedWireFile = XbdmParitySession.NewScratchWirePath(session.Managed, "payload.bin");
        var managedWireDir = managedWireFile[..managedWireFile.LastIndexOf('\\')];

        try
        {
            XbdmParitySession.EnsureScratchDirectory(session.Native, nativeWireFile);
            XbdmParitySession.EnsureScratchDirectory(session.Managed, managedWireFile);

            yield return ParityCompare.BothAction(
                FileCategory,
                "SendFile",
                () => session.Native.SendFile(localPayload, nativeWireFile),
                () => session.Managed.SendFile(localPayload, managedWireFile));

            var nativeAttr = session.Native.GetFileAttributes(nativeWireFile);
            var managedAttr = session.Managed.GetFileAttributes(managedWireFile);
            yield return nativeAttr.Size == managedAttr.Size && nativeAttr.Size == (ulong)payload.Length
                ? ParityCompare.Pass(FileCategory, "GetFileAttributes(uploaded)", $"{nativeAttr.Size} bytes")
                : ParityCompare.Fail(
                    FileCategory,
                    "GetFileAttributes(uploaded)",
                    "Uploaded size mismatch.",
                    nativeAttr.Size.ToString(),
                    managedAttr.Size.ToString());

            yield return ParityCompare.BothAction(
                FileCategory,
                "ReceiveFile",
                () => session.Native.ReceiveFile(nativeWireFile, nativeReceive),
                () => session.Managed.ReceiveFile(managedWireFile, managedReceive));

            var nativeRoundTrip = File.ReadAllBytes(nativeReceive);
            var managedRoundTrip = File.ReadAllBytes(managedReceive);
            yield return nativeRoundTrip.SequenceEqual(payload) && managedRoundTrip.SequenceEqual(payload)
                ? ParityCompare.Pass(FileCategory, "ReceiveFile(roundtrip)", $"{payload.Length} bytes")
                : ParityCompare.Fail(
                    FileCategory,
                    "ReceiveFile(roundtrip)",
                    "Roundtrip bytes differ.",
                    nativeRoundTrip.Length.ToString(),
                    managedRoundTrip.Length.ToString());

            yield return ParityCompare.BothAction(
                FileCategory,
                "SetFileAttributes(readonly)",
                () => session.Native.SetFileAttributes(nativeWireFile, XbdmConstants.AttrReadOnly),
                () => session.Managed.SetFileAttributes(managedWireFile, XbdmConstants.AttrReadOnly));

            var nativeReadonly = session.Native.GetFileAttributes(nativeWireFile).Attributes;
            var managedReadonly = session.Managed.GetFileAttributes(managedWireFile).Attributes;
            yield return (nativeReadonly & XbdmConstants.AttrReadOnly) != 0 &&
                         (managedReadonly & XbdmConstants.AttrReadOnly) != 0
                ? ParityCompare.Pass(FileCategory, "SetFileAttributes(readonly)", "readonly set")
                : ParityCompare.Fail(
                    FileCategory,
                    "SetFileAttributes(readonly)",
                    "Readonly flag mismatch.",
                    $"0x{nativeReadonly:x}",
                    $"0x{managedReadonly:x}");

            yield return ParityCompare.BothAction(
                FileCategory,
                "Delete(file,readonly)",
                () => session.Native.Delete(nativeWireFile, isDirectory: false),
                () => session.Managed.Delete(managedWireFile, isDirectory: false),
                acceptedErrors: ParityCompare.AcceptCannotAccess);

            yield return ParityCompare.BothAction(
                FileCategory,
                "SetFileAttributes(writable)",
                () => session.Native.SetFileAttributes(nativeWireFile, attributes: 0),
                () => session.Managed.SetFileAttributes(managedWireFile, attributes: 0));

            nativeReadonly = session.Native.GetFileAttributes(nativeWireFile).Attributes;
            managedReadonly = session.Managed.GetFileAttributes(managedWireFile).Attributes;
            yield return (nativeReadonly & XbdmConstants.AttrReadOnly) == 0 &&
                         (managedReadonly & XbdmConstants.AttrReadOnly) == 0
                ? ParityCompare.Pass(FileCategory, "SetFileAttributes(writable)", "readonly cleared")
                : ParityCompare.Fail(
                    FileCategory,
                    "SetFileAttributes(writable)",
                    "Readonly flag still set.",
                    $"0x{nativeReadonly:x}",
                    $"0x{managedReadonly:x}");

            yield return ParityCompare.BothAction(
                FileCategory,
                "Delete(file)",
                () => session.Native.Delete(nativeWireFile, isDirectory: false),
                () => session.Managed.Delete(managedWireFile, isDirectory: false));

            yield return ParityCompare.BothAction(
                FileCategory,
                "Delete(directory)",
                () => session.Native.Delete(nativeWireDir, isDirectory: true),
                () => session.Managed.Delete(managedWireDir, isDirectory: true));
        }
        finally
        {
            foreach (var path in new[] { localPayload, nativeReceive, managedReceive })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
