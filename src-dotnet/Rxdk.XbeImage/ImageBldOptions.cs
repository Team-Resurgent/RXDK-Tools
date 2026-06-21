namespace Rxdk.XbeImage;

public sealed class ImageBldNoPreloadEntry
{
    public required string SectionName { get; init; }
}

public sealed class ImageBldInsertFileEntry
{
    public required string FilePath { get; init; }
    public required string SectionName { get; init; }
    public bool NoPreload { get; init; }
    public bool ReadOnly { get; init; }
}

public sealed class ImageBldTestAlternateTitleId
{
    public uint TitleId { get; init; }
    public byte[]? SignatureKey { get; init; }
}

public enum ImageBldParseMode
{
    Build,
    Dump,
}

public sealed class ImageBldOptions
{
    public ImageBldParseMode Mode { get; set; } = ImageBldParseMode.Build;

    public string? InputFilePath { get; set; }
    public string? OutputFilePath { get; set; }

    public List<ImageBldNoPreloadEntry> NoPreloadSections { get; } = [];

    public List<ImageBldInsertFileEntry> InsertFiles { get; } = [];

    public bool EmitPeHeader { get; set; }

    public uint SizeOfStack { get; set; }

    public uint InitFlags { get; set; } = XbeImageConstants.XinitMountUtilityDrive;

    public bool LimitMemory { get; set; }
    public bool NoSetupHardDisk { get; set; }
    public bool DontModifyHardDisk { get; set; }
    public bool DontMountUtilityDrive { get; set; }
    public bool FormatUtilityDrive { get; set; }

    public uint UtilityDriveClusterSize { get; set; }

    public uint Version { get; set; }

    public uint TestGameRegion { get; set; } =
        XbeImageConstants.GameRegionNa |
        XbeImageConstants.GameRegionJapan |
        XbeImageConstants.GameRegionRestOfWorld |
        XbeImageConstants.GameRegionManufacturing;

    public uint TestAllowedMediaTypes { get; set; } =
        XbeImageConstants.MediaTypeHardDisk |
        XbeImageConstants.MediaTypeDvdCd |
        XbeImageConstants.MediaTypeMediaBoard;

    public uint TestGameRatings { get; set; } = XbeImageConstants.MaxUlong;

    public uint TestTitleId { get; set; }

    public List<ImageBldTestAlternateTitleId> TestAlternateTitleIds { get; } = [];

    public string TestTitleName { get; set; } = string.Empty;

    public byte[] TestLanKey { get; set; } = DefaultTestKey();

    public byte[] TestSignatureKey { get; set; } = DefaultTestKey();

    public string? TitleImage { get; set; }
    public string? TitleInfo { get; set; }
    public string? DefaultSaveImage { get; set; }

    public bool NoWarnLibraryApproval { get; set; }

    /// <summary>When set, overrides the image header timestamp (for deterministic tests).</summary>
    public uint? FixedTimeDateStamp { get; set; }

    public bool ShowUsage { get; set; }

    private static byte[] DefaultTestKey()
    {
        ReadOnlySpan<byte> pattern = "TEST"u8;
        var key = new byte[XbeImageConstants.CertificateKeyLength];
        for (var i = 0; i < key.Length; i++)
        {
            key[i] = pattern[i % pattern.Length];
        }

        return key;
    }
}
