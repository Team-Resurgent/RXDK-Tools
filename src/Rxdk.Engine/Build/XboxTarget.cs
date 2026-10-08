namespace Rxdk.Engine.Build;

/// <summary>
/// The original Xbox's code-generation flags, shared by every compile the engine runs (titles,
/// SDK libraries, generated assembly) so all objects that end up in one XBE agree on the ABI.
/// Rxdk.MsBuild's RxdkCompile carries the same list (it does not reference the engine).
/// </summary>
public static class XboxTarget
{
    /// <summary>
    /// <list type="bullet">
    /// <item><c>-march=pentium3</c>: the Xbox is a Coppermine Pentium III. CPUID reports SSE but
    /// not SSE2, so without it codegen picks an x86 baseline whose SSE2 encodings are invalid
    /// opcodes on the console.</item>
    /// <item><c>-mlong-double-64</c>: <c>long double</c> is the same 64-bit type as <c>double</c>,
    /// as it is with the XDK's MSVC compiler, the msvc-ABI SDK libraries (libxnet, libxonline)
    /// and the Xbox 360. The i686-pc-windows-gnu default is the 80-bit x87 type, which made
    /// <c>DECIMAL_DIG</c> 21 and mixed two <c>long double</c> layouts in one image.</item>
    /// </list>
    /// </summary>
    public static readonly IReadOnlyList<string> CodegenFlags = new[] { "-march=pentium3", "-mlong-double-64" };
}
