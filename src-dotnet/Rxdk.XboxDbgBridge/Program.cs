using System.Text.Json;
using Rxdk.XboxDbgBridge;

return Run(args);

static int Run(string[] args)
{
    BridgeBootstrap.RegisterBackend();
    using var session = new DebugBridgeSession();
    session.Initialize();

    string? line;
    while (session.IsActive && (line = Console.In.ReadLine()) is not null)
    {
        if (line.Length == 0)
            continue;

        var id = 0;
        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("id", out var idProperty) &&
                idProperty.TryGetInt32(out var parsedId))
            {
                id = parsedId;
            }
        }
        catch (JsonException)
        {
            // HandleCommand reports invalid json when needed.
        }

        session.HandleCommand(line, id);
    }

    return 0;
}
