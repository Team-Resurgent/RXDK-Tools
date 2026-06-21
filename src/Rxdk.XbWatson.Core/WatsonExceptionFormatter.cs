namespace Rxdk.XbWatson.Core;

public static class WatsonExceptionFormatter
{
    public static (string Line1, string Line2) Format(uint code, nuint address, bool writeViolation, nuint faultAddress)
    {
        switch (code)
        {
            case WatsonExceptionCodes.Breakpoint:
                return (
                    $"A breakpoint exception (0x{code:x8}) has been reached in",
                    $"the application at location 0x{(uint)address:x8}.");

            case WatsonExceptionCodes.AccessViolation:
                var line1 = $"The instruction at address 0x{(uint)address:x8} referenced memory";
                var line2 = writeViolation
                    ? $"at address 0x{(uint)faultAddress:x8}.  The memory could not be written."
                    : $"at address 0x{(uint)faultAddress:x8}.  The memory could not be read.";
                return (line1, line2);

            default:
                return (
                    $"An exception (0x{code:x8}) occurred in the application",
                    $"at location 0x{(uint)address:x8}");
        }
    }
}
