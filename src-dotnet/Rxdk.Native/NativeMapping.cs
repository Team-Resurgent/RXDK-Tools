using Rxdk.Xbdm;
using Abstractions = Rxdk.Xbdm;

namespace Rxdk.Native;

internal static class NativeMapping
{
    internal static Abstractions.XbdmDirEntry ToModel(XbdmDirEntry entry) =>
        new(entry.Name, entry.Size, entry.Attributes, entry.ChangeTimeUnix);

    internal static Abstractions.XbdmUser ToModel(XbdmUser user) =>
        new(user.UserName, user.AccessPrivileges);
}
