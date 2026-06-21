namespace Rxdk.Xbdm;

/// <summary>Selects which <see cref="IXbdmClient"/> implementation to use.</summary>
public static class XbdmClients
{
    private static readonly object Gate = new();
    private static Func<IXbdmClient> _factory = () => throw new InvalidOperationException(
        "No XBDM client registered. Reference Rxdk.Xbdm.Managed and ensure bootstrap is loaded.");

    public static IXbdmClient Create()
    {
        lock (Gate)
            return _factory();
    }

    public static void Register(Func<IXbdmClient> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (Gate)
            _factory = factory;
    }

    public static void Register<TClient>() where TClient : IXbdmClient, new()
    {
        Register(() => new TClient());
    }
}
