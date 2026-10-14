using System.Buffers.Binary;
using System.Text;
using Rxdk.Engine.Build;
using Xunit;

namespace Rxdk.Engine.Tests;

/// <summary>
/// libunwind finds a title's unwind tables only between ___eh_frame_start and ___eh_frame_end,
/// so the link must place them at the edges of .eh_frame. These pin the post-link check and the
/// marker sections that make the bracket hold.
/// </summary>
public sealed class EhFrameCheckTests
{
    [Fact]
    public void MarkersAtTheSectionEdges_Pass()
    {
        var pe = Pe(new[] { (".text", 0x100u), (".eh_frame", 0xDCu) },
                    new[] { ("___eh_frame_start", 0u, (short)2), ("___eh_frame_end", 0xDCu, (short)2) });
        Assert.Null(EhFrameCheck.Verify(pe));
    }

    [Fact]
    public void EndMarkerBeforeArchiveFdes_Fails()
    {
        // The layout a plain-.eh_frame end marker produced: archive members' FDEs after it.
        var pe = Pe(new[] { (".eh_frame", 0xDCu) },
                    new[] { ("___eh_frame_start", 0u, (short)1), ("___eh_frame_end", 0xA4u, (short)1) });
        var error = EhFrameCheck.Verify(pe);
        Assert.NotNull(error);
        Assert.Contains("+0x0..+0xA4", error);
    }

    [Fact]
    public void StartMarkerAfterTheSectionStart_Fails()
    {
        var pe = Pe(new[] { (".eh_frame", 0xDCu) },
                    new[] { ("___eh_frame_start", 0x10u, (short)1), ("___eh_frame_end", 0xDCu, (short)1) });
        Assert.NotNull(EhFrameCheck.Verify(pe));
    }

    [Fact]
    public void MarkersInDifferentSections_Fail()
    {
        var pe = Pe(new[] { (".rdata", 0x40u), (".eh_frame", 0xDCu) },
                    new[] { ("___eh_frame_start", 0u, (short)1), ("___eh_frame_end", 0xDCu, (short)2) });
        Assert.NotNull(EhFrameCheck.Verify(pe));
    }

    [Fact]
    public void MarkersOutsideEhFrame_Fail()
    {
        var pe = Pe(new[] { (".rdata", 0x40u) },
                    new[] { ("___eh_frame_start", 0u, (short)1), ("___eh_frame_end", 0x40u, (short)1) });
        Assert.NotNull(EhFrameCheck.Verify(pe));
    }

    [Fact]
    public void OnlyOneMarker_Fails()
    {
        var pe = Pe(new[] { (".eh_frame", 0xDCu) }, new[] { ("___eh_frame_start", 0u, (short)1) });
        Assert.NotNull(EhFrameCheck.Verify(pe));
    }

    [Fact]
    public void ImageWithoutMarkers_Passes()
    {
        // A title that does not link libcpp has no bracket to check.
        var pe = Pe(new[] { (".text", 0x100u) }, new[] { ("_main", 0u, (short)1) });
        Assert.Null(EhFrameCheck.Verify(pe));
    }

    [Fact]
    public void NotAPeImage_Fails()
    {
        Assert.NotNull(EhFrameCheck.Verify(new byte[0x80]));
    }

    [Fact]
    public void EndMarkerSortsAfterEveryPlainEhFrameContribution()
    {
        // The linker merges .eh_frame$z after all plain .eh_frame input sections, wherever the
        // object lands in the link order; the start marker is plain .eh_frame, linked first.
        Assert.Contains(".section .eh_frame$z,", XdkLink.EhEndAsm);
        Assert.Contains(".section .eh_frame,", XdkLink.EhBeginAsm);
    }

    [Fact]
    public void EndMarkerDefinesTheNameTheSdksLibunwindNeeds()
    {
        // The SDK's libunwind refers to the end marker by this name (build/generated/
        // rxdk_eh_frame_end.h in RXDK-Libs), so the pair links only with Tools that sort it last.
        Assert.Equal("___rxdk_eh_frame_end_requires_rxdk_tools_1_3_6", XdkLink.EhEndSdkSymbol);
        Assert.Contains(".globl " + XdkLink.EhEndSdkSymbol + "\n", XdkLink.EhEndAsm);
        Assert.Contains("___eh_frame_end:\n" + XdkLink.EhEndSdkSymbol + ":\n", XdkLink.EhEndAsm);
    }

    // A minimal PE: DOS header, COFF header, section table, COFF symbols and string table. Names
    // longer than eight bytes go through the string table, as the linker writes them.
    private static byte[] Pe((string Name, uint Size)[] sections, (string Name, uint Value, short Section)[] symbols)
    {
        var strings = new List<byte>();
        int AddString(string s)
        {
            int offset = 4 + strings.Count;
            strings.AddRange(Encoding.ASCII.GetBytes(s));
            strings.Add(0);
            return offset;
        }

        const int peOff = 0x40;
        int secTable = peOff + 24;
        int symPtr = secTable + sections.Length * 40;
        int strTab = symPtr + symbols.Length * 18;
        var image = new byte[strTab];
        image[0] = (byte)'M'; image[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x3C), peOff);
        "PE\0\0"u8.CopyTo(image.AsSpan(peOff));
        var coff = image.AsSpan(peOff + 4);
        BinaryPrimitives.WriteUInt16LittleEndian(coff, 0x14C);
        BinaryPrimitives.WriteUInt16LittleEndian(coff[2..], (ushort)sections.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(coff[8..], (uint)symPtr);
        BinaryPrimitives.WriteUInt32LittleEndian(coff[12..], (uint)symbols.Length);

        for (int i = 0; i < sections.Length; i++)
        {
            var s = image.AsSpan(secTable + i * 40, 40);
            var name = sections[i].Name.Length <= 8 ? sections[i].Name : "/" + AddString(sections[i].Name);
            Encoding.ASCII.GetBytes(name).CopyTo(s);
            BinaryPrimitives.WriteUInt32LittleEndian(s[8..], sections[i].Size);
        }
        for (int i = 0; i < symbols.Length; i++)
        {
            var e = image.AsSpan(symPtr + i * 18, 18);
            if (symbols[i].Name.Length <= 8) Encoding.ASCII.GetBytes(symbols[i].Name).CopyTo(e);
            else BinaryPrimitives.WriteUInt32LittleEndian(e[4..], (uint)AddString(symbols[i].Name));
            BinaryPrimitives.WriteUInt32LittleEndian(e[8..], symbols[i].Value);
            BinaryPrimitives.WriteInt16LittleEndian(e[12..], symbols[i].Section);
        }

        var size = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)(4 + strings.Count));
        return image.Concat(size).Concat(strings).ToArray();
    }
}
