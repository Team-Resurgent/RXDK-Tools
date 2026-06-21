namespace Rxdk.KitConfig.Stores;

public interface IConsoleAddressStore
{
    void SetAddress(string consoleName, string ipAddress);
    string? TryGetAddress(string consoleName);
    void RemoveAddress(string consoleName);
}
