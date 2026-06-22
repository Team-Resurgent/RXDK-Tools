namespace Rxdk.Xbdm.Tests.Hardware;

/// <summary>
/// Finds a writable probe offset in an on-disk XBE without calling SECTIONS on the kit
/// (SECTIONS is unavailable while a title is loaded under the debugger).
/// </summary>
internal static class XbeImageProbe
{
    private const uint Signature = 0x48454258;
    private const uint SectionWriteable = 0x00000001;
    private const uint SectionHeadPageReadonly = 0x00000010;
    private const uint SectionTailPageReadonly = 0x00000020;
    private const int EncryptedDigestSize = 256;
    private const int SectionHeaderSize = 56;

    public static nuint? TryGetWritableProbeAddress(string xbePath, nuint moduleBase)
    {
        foreach (var address in EnumerateWritableProbeAddresses(xbePath, moduleBase))
            return address;

        return null;
    }

    public static IEnumerable<nuint> EnumerateWritableProbeAddresses(string xbePath, nuint moduleBase)
    {
        if (!File.Exists(xbePath))
            yield break;

        var data = File.ReadAllBytes(xbePath);
        if (data.Length < 300)
            yield break;

        if (BitConverter.ToUInt32(data, 0) != Signature)
            yield break;

        var imageBase = BitConverter.ToUInt32(data, EncryptedDigestSize + 4);
        var numberOfSections = BitConverter.ToUInt32(data, EncryptedDigestSize + 28);
        var sectionHeadersVa = BitConverter.ToUInt32(data, EncryptedDigestSize + 32);
        if (numberOfSections == 0 || sectionHeadersVa < imageBase)
            yield break;

        var sectionTableOffset = (int)(sectionHeadersVa - imageBase);
        if (sectionTableOffset < 0 ||
            sectionTableOffset + (long)numberOfSections * SectionHeaderSize > data.Length)
        {
            yield break;
        }

        var probes = new List<(nuint Address, uint Size)>();
        for (var i = 0; i < numberOfSections; i++)
        {
            var offset = sectionTableOffset + i * SectionHeaderSize;
            var flags = BitConverter.ToUInt32(data, offset);
            var virtualAddress = BitConverter.ToUInt32(data, offset + 4);
            var virtualSize = BitConverter.ToUInt32(data, offset + 8);
            if ((flags & SectionWriteable) == 0 || virtualSize < 0x200)
                continue;

            // Skip the lead image section (typically .text at the image base).
            if (virtualAddress < imageBase + 0x70000)
                continue;

            var probeOffset = 0x100u;
            if ((flags & SectionHeadPageReadonly) != 0)
                probeOffset = Math.Max(probeOffset, 0x1000u);

            if ((flags & SectionTailPageReadonly) != 0 && virtualSize > 0x1000 &&
                probeOffset > virtualSize - 0x1000u - 4u)
            {
                continue;
            }

            if (probeOffset + 4 > virtualSize)
                probeOffset = virtualSize / 2u;

            var imageOffset = (nuint)(virtualAddress - imageBase + probeOffset);
            probes.Add((moduleBase + imageOffset, virtualSize));
        }

        foreach (var probe in probes.OrderByDescending(p => p.Size))
            yield return probe.Address;
    }
}
