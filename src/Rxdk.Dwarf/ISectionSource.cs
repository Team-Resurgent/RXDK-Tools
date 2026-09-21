// SPDX-License-Identifier: GPL-3.0-or-later
// Part of RXDK-Tools - see LICENSE for the full GNU GPL v3.

namespace Rxdk.Dwarf;

/// <summary>
/// A container (PE or ELF) the DWARF reader pulls <c>.debug_*</c> sections from.
/// Lets the same reader run over an original-Xbox PE (<see cref="PeImage"/>,
/// little-endian) or, later, a big-endian ELF, without knowing the file format.
/// </summary>
public interface ISectionSource
{
    /// <summary>The bytes of a named section, or an empty array if absent.</summary>
    byte[] Section(string name);
    bool HasSection(string name);
    IEnumerable<string> SectionNames { get; }

    /// <summary>True when the image's data is big-endian (DWARF follows the target
    /// byte order). Original Xbox PE = false; PowerPC ELF = true.</summary>
    bool IsBigEndian { get; }

    /// <summary>The image entry point (RVA/VA), for reference.</summary>
    ulong Entry { get; }
}
