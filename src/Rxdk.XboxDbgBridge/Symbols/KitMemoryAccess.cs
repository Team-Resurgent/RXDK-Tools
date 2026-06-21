namespace Rxdk.XboxDbgBridge.Symbols;

internal readonly struct KitMemoryAccess
{
    internal required Func<nuint, uint?> ReadDword { get; init; }
    internal required Func<nuint, byte?> ReadByte { get; init; }
}
