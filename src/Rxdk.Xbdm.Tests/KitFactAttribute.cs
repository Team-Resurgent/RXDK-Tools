using Xunit;

namespace Rxdk.Xbdm.Tests;

/// <summary>
/// A test that needs a live kit. Without <c>RXDK_TEST_CONSOLE</c> (or the other environment
/// variable it names, such as <c>RXDK_TEST_PASSWORD</c>) it is reported as skipped, with the
/// reason, rather than passing without having run.
/// </summary>
public sealed class KitFactAttribute : FactAttribute
{
    public KitFactAttribute(string? alsoNeeds = null)
    {
        if (!XbdmTestEnvironment.TryGetConsole(out _))
            Skip = "Needs a kit: set RXDK_TEST_CONSOLE (name or IP).";
        else if (alsoNeeds is not null && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(alsoNeeds)))
            Skip = $"Needs {alsoNeeds}.";
    }
}
