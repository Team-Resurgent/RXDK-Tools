namespace Rxdk.Pdb.Dbi;

/// <summary>
/// One module (compiland / .obj) described by the DBI module-info substream. The
/// <see cref="SymbolStreamIndex"/> points at the MSF stream that holds this module's CodeView
/// symbols (procedures, locals, def-ranges); it is -1 when the module has no symbols.
/// </summary>
public sealed class DbiModule
{
    public required int Index { get; init; }
    public required string ModuleName { get; init; }
    public required string ObjectFileName { get; init; }
    public required int SymbolStreamIndex { get; init; }
    public required uint SymbolByteSize { get; init; }

    /// <summary>
    /// Sizes of the C11 and C13 line-info substreams that follow the symbols in the module's MSF
    /// stream. Layout is [4-byte CV signature][symbols: SymbolByteSize-4][C11: C11ByteSize][C13:
    /// C13ByteSize]. NOTE: C11/C13 are CodeView debug-format versions (CV_SIGNATURE_C11 = 1,
    /// CV_SIGNATURE_C13 = 4), NOT C language standards. clang -gcodeview and modern MSVC write C13
    /// for any source language; the XDK's VC 7.1 compiler wrote C11 (TriangleXDK.pdb has only C11).
    /// Only C13 line info is read.
    /// </summary>
    public required uint C11ByteSize { get; init; }
    public required uint C13ByteSize { get; init; }

    /// <summary>Byte offset of the C13 line-info substream within the module's MSF stream.</summary>
    public int C13LineInfoOffset => (int)SymbolByteSize + (int)C11ByteSize;

    /// <summary>Primary section contribution (section:offset span) attributed to this module.</summary>
    public required SectionContribution Contribution { get; init; }

    public bool HasSymbols => SymbolStreamIndex >= 0 && SymbolByteSize > 0;

    public bool HasLineInfo => SymbolStreamIndex >= 0 && C13ByteSize > 0;
}

/// <summary>A (section, offset, size) span of image bytes attributed to a module.</summary>
public readonly record struct SectionContribution(ushort Section, int Offset, int Size, ushort ModuleIndex);
