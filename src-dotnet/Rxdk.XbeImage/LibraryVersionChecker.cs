namespace Rxdk.XbeImage;

internal static class LibraryVersionChecker
{
    private readonly record struct VersionRange(ushort MinVer, ushort MaxVer, bool Expired = false, bool MaxApproved = false);

    private readonly record struct QfeEntry(string LibName, ushort QfeBuild, ushort QfeNumber, bool Mandatory);

    private readonly record struct DependencyEntry(
        string DependentName,
        ushort DependentNotOlderThan,
        ushort DependentNotNewerThan,
        string SupportingName,
        ushort MinimumSupportingVersion);

    private static readonly VersionRange[] VersionRanges =
    {
        new(3911, 3999, Expired: true),
        new(4039, 4099),
        new(4134, 4199),
        new(4242, 4299),
        new(4361, 65535),
        new(4400, 0),
        new(0, 0),
    };

    private static readonly QfeEntry[] QfeEntries =
    {
        new("DSOUND\0\0", 3925, 1, true),
        new("DMUSIC\0\0", 3925, 1, false),
        new("DSOUND\0\0", 3936, 1, false),
        new("DMUSIC\0\0", 3941, 1, false),
        new("D3D8\0\0\0\0", 3948, 1, true),
        new("D3D8LTCG", 3948, 1, true),
        new("DSOUND\0\0", 3949, 1, false),
        new("XAPILIB\0", 3950, 1, true),
        new("D3D8\0\0\0\0", 4039, 2, true),
        new("D3D8LTCG", 4039, 2, true),
        new("DSOUND\0\0", 4039, 3, false),
        new("XAPILIB\0", 4039, 4, true),
        new("D3D8\0\0\0\0", 4134, 2, false),
        new("D3D8LTCG", 4134, 2, false),
        new("DSOUND\0\0", 4134, 3, false),
        new("DSOUND\0\0", 4134, 5, false),
        new("DSOUND\0\0", 4134, 6, true),
        new("XAPILIB\0", 4134, 6, true),
        new("WMVDEC\0\0", 4134, 6, false),
    };

    private static readonly DependencyEntry[] Dependencies =
    {
        new("DMUSIC\0\0", 3911, 3925, "DSOUND\0\0", 3936),
    };

    public static int CheckLibraryApprovalStatus(
        XbeImageLibraryVersion? xapiVersion,
        XbeImageLibraryVersion[] libraries,
        Action<XbeImageLibraryVersion, int>? onWarning)
    {
        var iverXapi = -1;
        var expired = false;
        if (xapiVersion is not null)
        {
            for (var i = 0; VersionRanges[i].MinVer != 0; i++)
            {
                if (AcceptableVerBuild(i, xapiVersion.Value.BuildVersion))
                {
                    expired = VersionRanges[i].Expired;
                    iverXapi = i;
                    break;
                }
            }

            if (iverXapi < 0 ||
                VersionRanges[iverXapi].MinVer == 0 ||
                xapiVersion.Value.MajorVersion != 1 ||
                xapiVersion.Value.MinorVersion != 0)
            {
                iverXapi = -1;
            }
        }

        var total = 2;
        foreach (var library in libraries)
        {
            var status = iverXapi < 0 ? 0 : -1;
            if (library.MajorVersion != 1 || library.MinorVersion != 0)
            {
                status = 0;
            }

            if (status != 0)
            {
                foreach (var qfe in QfeEntries)
                {
                    if (LibraryNameMatches(library.LibraryName, qfe.LibName) &&
                        AcceptableVerBuild(iverXapi, qfe.QfeBuild))
                    {
                        if (qfe.Mandatory &&
                            (library.BuildVersion < qfe.QfeBuild ||
                             (library.BuildVersion == qfe.QfeBuild && library.QfeVersion < qfe.QfeNumber)))
                        {
                            status = 0;
                        }
                        else if (library.BuildVersion == qfe.QfeBuild && library.QfeVersion == qfe.QfeNumber)
                        {
                            status = 2;
                        }
                    }
                }
            }

            if (status < 0)
            {
                if (iverXapi >= 0 &&
                    VersionRanges[iverXapi].MinVer == library.BuildVersion &&
                    library.QfeVersion == 1)
                {
                    status = 2;
                }

                if (status < 0)
                {
                    status = 0;
                }
            }

            if (library.ApprovedLibrary < status)
            {
                status = library.ApprovedLibrary;
            }

            if (status > 0)
            {
                foreach (var dep in Dependencies)
                {
                    if (LibraryNameMatches(library.LibraryName, dep.DependentName) &&
                        library.BuildVersion >= dep.DependentNotOlderThan &&
                        library.BuildVersion <= dep.DependentNotNewerThan)
                    {
                        var found = false;
                        foreach (var candidate in libraries)
                        {
                            if (!candidate.Equals(library) &&
                                LibraryNameMatches(candidate.LibraryName, dep.SupportingName) &&
                                AcceptableVerBuild(iverXapi, candidate.BuildVersion) &&
                                candidate.BuildVersion >= dep.MinimumSupportingVersion)
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            status = 0;
                        }
                    }
                }
            }

            if (status == 2 && expired)
            {
                status = -1;
            }

            if (status != 2)
            {
                onWarning?.Invoke(library, status);
            }

            if (status <= 0 && onWarning == null)
            {
                return 0;
            }

            if (status < total)
            {
                total = status;
            }
        }

        return total < 0 ? 0 : total;
    }

    private static bool AcceptableVerBuild(int index, ushort version)
    {
        if (index < 0)
        {
            return false;
        }

        var range = VersionRanges[index];
        if (version < range.MinVer)
        {
            return false;
        }

        if (range.MaxVer == 0)
        {
            return true;
        }

        return range.MaxApproved ? version <= range.MaxVer : version < range.MaxVer;
    }

    private static bool LibraryNameMatches(byte[] libraryName, string expected)
    {
        var expectedBytes = System.Text.Encoding.ASCII.GetBytes(expected);
        if (expectedBytes.Length > XbeImageConstants.LibraryVersionNameLength)
        {
            expectedBytes = expectedBytes[..XbeImageConstants.LibraryVersionNameLength];
        }

        for (var i = 0; i < XbeImageConstants.LibraryVersionNameLength; i++)
        {
            var expectedByte = i < expectedBytes.Length ? expectedBytes[i] : (byte)0;
            if (libraryName[i] != expectedByte)
            {
                return false;
            }
        }

        return true;
    }
}
