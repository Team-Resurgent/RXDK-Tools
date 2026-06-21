using Rxdk.KitConfig;
using Rxdk.Xbdm;
using Rxdk.Xbdm.KitServices.Services;
using Rxdk.Xbdm.Managed;

namespace Rxdk.XbFile;

public sealed class XbConsoleSession : IDisposable
{
    private readonly XbdmConnection? _connection;

    public string ConsoleName { get; }

    private XbConsoleSession(XbdmConnection connection, string consoleName)
    {
        _connection = connection;
        ConsoleName = consoleName;
    }

    public XbdmConnection Connection => _connection ?? throw new InvalidOperationException("No Xbox connection.");

    public static XbConsoleSession Connect(string? xboxTarget)
    {
        XbdmSession.EnsureInitialized();

        var target = xboxTarget?.Trim();
        if (string.IsNullOrWhiteSpace(target))
            target = Environment.GetEnvironmentVariable("XBOXIP");

        if (!string.IsNullOrWhiteSpace(target))
        {
            var result = new SetDefaultConsoleService().Execute(target);
            return new XbConsoleSession(XbdmSession.Connect(result.RegistryName), result.RegistryName);
        }

        var config = KitConfigProvider.CreateDefault();
        var name = config.Consoles.GetDefaultConsoleName();
        if (string.IsNullOrWhiteSpace(name))
            throw new XbFileException("No default Xbox console. Use -x, set XBOXIP, or run xbset.");

        return new XbConsoleSession(XbdmSession.Connect(name), name);
    }

    public static bool PathNeedsXbox(IEnumerable<XbPath> paths) => paths.Any(p => p.IsXbox);

    public void Dispose() => _connection?.Dispose();
}
