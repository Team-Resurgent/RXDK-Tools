using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Tests.Hardware;

internal static partial class XbdmKitChecks
{
    private static IEnumerable<KitCheckResult> ExecuteFileRoundTrip(XbdmKitSession session)
    {
        var localPayload = Path.Combine(Path.GetTempPath(), $"xbdm-kit-{Guid.NewGuid():N}.bin");
        var receivePath = Path.Combine(Path.GetTempPath(), $"xbdm-rx-{Guid.NewGuid():N}.bin");
        var payload = Enumerable.Range(0, 4096).Select(i => (byte)(i & 0xFF)).ToArray();
        File.WriteAllBytes(localPayload, payload);

        var wireFile = XbdmKitSession.NewScratchWirePath(session.Managed, "payload.bin");
        var wireDir = wireFile[..wireFile.LastIndexOf('\\')];

        try
        {
            XbdmKitSession.EnsureScratchDirectory(session.Managed, wireFile);

            yield return KitCheck.ManagedAction(
                FileCategory,
                "SendFile",
                () => session.Managed.SendFile(localPayload, wireFile));

            var attr = session.Managed.GetFileAttributes(wireFile);
            yield return attr.Size == (ulong)payload.Length
                ? KitCheck.Pass(FileCategory, "GetFileAttributes(uploaded)", $"{attr.Size} bytes")
                : KitCheck.Fail(FileCategory, "GetFileAttributes(uploaded)", "Uploaded size mismatch.", attr.Size.ToString());

            yield return KitCheck.ManagedAction(
                FileCategory,
                "ReceiveFile",
                () => session.Managed.ReceiveFile(wireFile, receivePath));

            var roundTrip = File.ReadAllBytes(receivePath);
            yield return roundTrip.SequenceEqual(payload)
                ? KitCheck.Pass(FileCategory, "ReceiveFile(roundtrip)", $"{payload.Length} bytes")
                : KitCheck.Fail(FileCategory, "ReceiveFile(roundtrip)", "Roundtrip bytes differ.", roundTrip.Length.ToString());

            yield return KitCheck.ManagedAction(
                FileCategory,
                "SetFileAttributes(readonly)",
                () => session.Managed.SetFileAttributes(wireFile, XbdmConstants.AttrReadOnly));

            var readonlyAttr = session.Managed.GetFileAttributes(wireFile).Attributes;
            yield return (readonlyAttr & XbdmConstants.AttrReadOnly) != 0
                ? KitCheck.Pass(FileCategory, "SetFileAttributes(readonly)", "readonly set")
                : KitCheck.Fail(FileCategory, "SetFileAttributes(readonly)", "Readonly flag mismatch.", $"0x{readonlyAttr:x}");

            yield return KitCheck.ManagedAction(
                FileCategory,
                "Delete(file,readonly)",
                () => session.Managed.Delete(wireFile, isDirectory: false),
                acceptedErrors: KitCheck.AcceptCannotAccess);

            yield return KitCheck.ManagedAction(
                FileCategory,
                "SetFileAttributes(writable)",
                () => session.Managed.SetFileAttributes(wireFile, attributes: 0));

            readonlyAttr = session.Managed.GetFileAttributes(wireFile).Attributes;
            yield return (readonlyAttr & XbdmConstants.AttrReadOnly) == 0
                ? KitCheck.Pass(FileCategory, "SetFileAttributes(writable)", "readonly cleared")
                : KitCheck.Fail(FileCategory, "SetFileAttributes(writable)", "Readonly flag still set.", $"0x{readonlyAttr:x}");

            yield return KitCheck.ManagedAction(
                FileCategory,
                "Delete(file)",
                () => session.Managed.Delete(wireFile, isDirectory: false));

            yield return KitCheck.ManagedAction(
                FileCategory,
                "Delete(directory)",
                () => session.Managed.Delete(wireDir, isDirectory: true));
        }
        finally
        {
            foreach (var path in new[] { localPayload, receivePath })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
