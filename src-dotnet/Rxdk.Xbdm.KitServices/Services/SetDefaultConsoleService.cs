using Rxdk.KitConfig;
using Rxdk.KitConfig.Stores;
using Rxdk.Xbdm.KitServices.Models;

namespace Rxdk.Xbdm.KitServices.Services;

public sealed class SetDefaultConsoleService
{
    private readonly AddConsoleService _addConsoleService = new();

    public sealed record SetDefaultConsoleResult(
        string RegistryName,
        string ConnectTarget,
        string? IpAddress,
        bool WasAlreadyRegistered,
        bool AddressUpdated);

    public SetDefaultConsoleResult Execute(string connectTarget, KitConfigProvider? config = null)
    {
        if (string.IsNullOrWhiteSpace(connectTarget))
            throw new ArgumentException("Xbox hostname or IP address is required.", nameof(connectTarget));

        var target = connectTarget.Trim();
        var state = _addConsoleService.ProbeConsole(target);
        if (!state.ConsoleIsValid)
            throw new InvalidOperationException($"Could not connect to Xbox at '{target}'. Is the kit powered on and reachable?");

        var wireName = ResolveRegistryName(state);
        var ipText = state.IpAddress.HasValue
            ? FormattingHelper.FormatIpAddress(state.IpAddress.Value)
            : null;

        config ??= KitConfigProvider.CreateDefault();
        var registryName = FindExistingConsoleName(config, wireName, ipText) ?? wireName;
        var wasAlreadyRegistered = config.Consoles.GetConsoleNames()
            .Any(n => string.Equals(n, registryName, StringComparison.OrdinalIgnoreCase));

        config.Consoles.AddConsole(registryName);

        var addressUpdated = false;
        if (!string.IsNullOrWhiteSpace(ipText))
        {
            var cached = config.Addresses.TryGetAddress(registryName);
            if (!string.Equals(cached, ipText, StringComparison.OrdinalIgnoreCase))
            {
                config.Addresses.SetAddress(registryName, ipText);
                addressUpdated = true;
            }
        }

        config.Consoles.SetDefaultConsole(registryName);

        return new SetDefaultConsoleResult(
            registryName,
            target,
            ipText,
            wasAlreadyRegistered,
            addressUpdated);
    }

    public static string? FindExistingConsoleName(
        KitConfigProvider config,
        string wireName,
        string? ipAddress)
    {
        var names = config.Consoles.GetConsoleNames();
        var existingByName = names.FirstOrDefault(n =>
            string.Equals(n, wireName, StringComparison.OrdinalIgnoreCase));
        if (existingByName != null)
            return existingByName;

        if (string.IsNullOrWhiteSpace(ipAddress))
            return null;

        return names.FirstOrDefault(n =>
            string.Equals(config.Addresses.TryGetAddress(n), ipAddress, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveRegistryName(AddConsoleWizardState state)
    {
        if (!string.IsNullOrWhiteSpace(state.WireName))
            return state.WireName.Trim();

        return state.ConsoleName.Trim();
    }
}
