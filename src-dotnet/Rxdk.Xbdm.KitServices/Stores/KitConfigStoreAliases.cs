namespace Rxdk.Xbdm.KitServices.Stores;

public interface IConsoleStore : Rxdk.KitConfig.Stores.IConsoleStore;

public class JsonConsoleStore : Rxdk.KitConfig.Stores.JsonConsoleStore;

/// <summary>
/// Registry-backed console list (Windows shell extension layout).
/// </summary>
public class ShellExtensionConsoleStore : Rxdk.KitConfig.Stores.RegistryConsoleStore;

public sealed class ShellExtensionConsoleAddressStore : Rxdk.KitConfig.Stores.RegistryConsoleAddressStore;
