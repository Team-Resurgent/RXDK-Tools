using Microsoft.Win32;
using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Managed;

public sealed class ManagedXbdmClient : IXbdmClient
{
    private static readonly object NameLock = new();
    private static string? _defaultConsoleName;
    private int _initCount;
    private readonly object _initLock = new();

    public string BackendName => "managed";

    public void Initialize()
    {
        lock (_initLock)
        {
            _initCount++;
        }
    }

    public void Shutdown()
    {
        lock (_initLock)
        {
            if (_initCount <= 0)
                return;
            _initCount--;
            if (_initCount == 0)
                XbdmSciRegistry.DisposeAll();
        }
    }

    public void Dispose() => Shutdown();

    public string GetDefaultConsoleName()
    {
        lock (NameLock)
        {
            if (!string.IsNullOrWhiteSpace(_defaultConsoleName))
                return _defaultConsoleName;

            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\XboxSDK");
                var value = key?.GetValue("XboxName") as string;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _defaultConsoleName = value;
                    return value;
                }
            }

            throw XbdmException.FromHResult("No default Xbox console name is configured.", XbdmHResults.NoXboxName);
        }
    }

    public void SetDefaultConsoleName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (NameLock)
        {
            _defaultConsoleName = name;
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\XboxSDK");
                key?.SetValue("XboxName", name);
            }
        }
    }

    public IXbdmConnection Connect(string consoleName) =>
        Connect(consoleName, BuildConnectOptions());

    public IXbdmConnection Connect(string consoleName, XbdmConnectOptions? options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consoleName);
        options ??= BuildConnectOptions();
        var session = XbdmProtocolSession.Connect(consoleName, TimeSpan.FromSeconds(10), options);
        return new ManagedXbdmConnection(consoleName, session, options);
    }

    private static XbdmConnectOptions BuildConnectOptions()
    {
        var password = Environment.GetEnvironmentVariable("RXDK_XBDM_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            return new XbdmConnectOptions();

        return new XbdmConnectOptions { AdminPassword = password };
    }
}
