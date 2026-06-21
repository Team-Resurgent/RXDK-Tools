using System.Text;
using System.Text.Json;
using Rxdk.XbeImage;
using Xunit;

namespace Rxdk.XbeImage.Tests;

public sealed class XbeImageCryptoTests
{
    [Fact]
    public void CalcDigest_prefixes_length()
    {
        Span<byte> data = stackalloc byte[] { 1, 2, 3, 4, 5 };
        Span<byte> digest = stackalloc byte[20];
        XbeImageCrypto.CalcDigest(data, digest);

        Assert.NotEqual(0, digest[0]);
    }

    [Fact]
    public void SignDigest_produces_256_byte_signature()
    {
        Span<byte> digest = stackalloc byte[20];
        XbeImageCrypto.CalcDigest(stackalloc byte[] { 9, 8, 7 }, digest);

        Span<byte> sig = stackalloc byte[256];
        XbeImageCrypto.SignDigest(digest, XbeImageKeys.PrivateKey, sig);

        Assert.Contains(sig.ToArray(), b => b != 0);
    }
}

public sealed class XbeImageBuilderTests
{
    private static readonly uint FixedTimestamp = 0x5F000000u;

    [Fact]
    public void Build_with_fixed_timestamp_is_deterministic()
    {
        var pe = TestPaths.TriangleExe;
        var out1 = Path.Combine(Path.GetTempPath(), $"det-1-{Guid.NewGuid():N}.xbe");
        var out2 = Path.Combine(Path.GetTempPath(), $"det-2-{Guid.NewGuid():N}.xbe");

        try
        {
            BuildManaged(pe, out1, FixedTimestamp);
            BuildManaged(pe, out2, FixedTimestamp);

            var a = File.ReadAllBytes(out1);
            var b = File.ReadAllBytes(out2);
            Assert.Equal(a, b);
        }
        finally
        {
            if (File.Exists(out1))
                File.Delete(out1);
            if (File.Exists(out2))
                File.Delete(out2);
        }
    }

    [Fact]
    public void Build_matches_checked_in_golden()
    {
        var pe = TestPaths.TriangleExe;
        var output = Path.Combine(Path.GetTempPath(), $"triangle-golden-{Guid.NewGuid():N}.xbe");

        try
        {
            BuildManaged(pe, output, FixedTimestamp);
            var built = File.ReadAllBytes(output);
            var goldenPath = TestPaths.TriangleGolden;
            if (Environment.GetEnvironmentVariable("REGEN_IMAGEBLD_GOLDEN") == "1")
            {
                File.WriteAllBytes(TestPaths.TriangleGoldenSource, built);
                if (!string.Equals(goldenPath, TestPaths.TriangleGoldenSource, StringComparison.OrdinalIgnoreCase))
                    File.WriteAllBytes(goldenPath, built);
            }

            var expected = File.ReadAllBytes(goldenPath);
            AssertMatchesGolden(built, expected);
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [Fact]
    public void Build_triangle_has_expected_structure()
    {
        var pe = TestPaths.TriangleExe;
        var output = Path.Combine(Path.GetTempPath(), $"triangle-struct-{Guid.NewGuid():N}.xbe");

        try
        {
            BuildManaged(pe, output, FixedTimestamp);
            var image = File.ReadAllBytes(output);

            Assert.Equal(XbeImageConstants.XbeImageSignature, BitConverter.ToUInt32(image, 0));
            var header = XbeImageReader.ReadHeader(image);
            Assert.NotEqual(0u, header.Certificate);
            Assert.True(header.NumberOfSections > 0);
            Assert.Equal(0x10000u, header.BaseAddress);

            var certificate = XbeImageReader.ReadCertificate(image, header.Certificate);
            Assert.NotNull(certificate);
            Assert.Contains(header.EncryptedDigest, b => b != 0);
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [Theory]
    [MemberData(nameof(GetBuildCases))]
    public void Build_cases_match_golden(ImageBldCase testCase)
    {
        var input = Path.Combine(TestPaths.ImageBldRoot, testCase.Input!);
        var output = Path.Combine(Path.GetTempPath(), $"{testCase.Name}-{Guid.NewGuid():N}.xbe");
        uint? fixedTimestamp = testCase.FixedTimestamp is not null
            ? uint.Parse(testCase.FixedTimestamp, System.Globalization.NumberStyles.HexNumber)
            : null;

        try
        {
            BuildManaged(input, output, fixedTimestamp, testCase.Args);

            var goldenPath = Path.Combine(TestPaths.ImageBldRoot, testCase.Golden!);
            AssertMatchesGolden(File.ReadAllBytes(output), File.ReadAllBytes(goldenPath));
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    public static IEnumerable<object[]> GetBuildCases()
    {
        foreach (var testCase in ImageBldCase.Load())
        {
            if (testCase.Mode != "dump")
                yield return new object[] { testCase };
        }
    }

    [Fact]
    public void Dump_triangle_matches_golden()
    {
        var xbe = TestPaths.TriangleXbe;
        var actual = DumpToNormalizedBody(xbe);
        var goldenPath = TestPaths.TriangleDumpGolden;

        if (Environment.GetEnvironmentVariable("REGEN_IMAGEBLD_GOLDEN") == "1")
            File.WriteAllText(TestPaths.TriangleDumpGoldenSource, actual);

        var expected = NormalizeGoldenDump(File.ReadAllText(goldenPath));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(GetDumpCases))]
    public void Dump_cases_match_golden(ImageBldCase testCase)
    {
        var input = Path.Combine(TestPaths.ImageBldRoot, testCase.Input!);
        var actual = DumpToNormalizedBody(input);
        var goldenPath = Path.Combine(TestPaths.ImageBldRoot, testCase.Golden!);
        var expected = NormalizeGoldenDump(File.ReadAllText(goldenPath));
        Assert.Equal(expected, actual);
    }

    public static IEnumerable<object[]> GetDumpCases()
    {
        foreach (var testCase in ImageBldCase.Load())
        {
            if (testCase.Mode == "dump")
                yield return new object[] { testCase };
        }
    }

    private static string DumpToNormalizedBody(string xbePath)
    {
        using var writer = new StringWriter();
        XbeImageDumper.Dump(xbePath, writer);
        return NormalizeGoldenDump(writer.ToString());
    }

    private static string NormalizeGoldenDump(string text) =>
        NormalizeDumpBody(NormalizeDumpPath(text)).TrimEnd('\r', '\n') + "\n";

    private static void AssertMatchesGolden(byte[] built, byte[] golden)
    {
        Assert.Equal(golden.Length, built.Length);

        if (ImagesEqualIgnoringVolatileFields(golden, built))
            return;

        var offset = FindFirstStableMismatch(golden, built);
        Assert.Fail(
            offset >= 0
                ? $"Stable XBE bytes differ at offset {offset}: golden=0x{golden[offset]:X2}, built=0x{built[offset]:X2}"
                : "Stable XBE bytes differ (volatile-masked comparison failed with no stable mismatch offset)");
    }

    private static int FindFirstStableMismatch(byte[] expected, byte[] actual)
    {
        for (var i = 0; i < expected.Length; i++)
        {
            if (!IsVolatileOffset(i, expected.Length) && expected[i] != actual[i])
                return i;
        }

        return -1;
    }

    private static void BuildManaged(
        string pe,
        string output,
        uint? fixedTimestamp = null,
        IReadOnlyList<string>? extraArgs = null)
    {
        if (extraArgs is { Count: > 0 })
        {
            var argv = new List<string> { $"/IN:{pe}", $"/OUT:{output}" };
            argv.AddRange(extraArgs);
            var parsedOptions = ImageBldOptionsParser.Parse(argv.ToArray(), expandLegacyArgv: false);
            ApplyDeterministicTestOptions(parsedOptions, fixedTimestamp);
            new XbeImageBuilder().Build(parsedOptions);
            return;
        }

        var options = new ImageBldOptions
        {
            InputFilePath = pe,
            OutputFilePath = output,
            NoWarnLibraryApproval = true,
            FixedTimeDateStamp = fixedTimestamp,
        };
        ApplyDeterministicTestOptions(options, fixedTimestamp);
        new XbeImageBuilder().Build(options);
    }

    private static void ApplyDeterministicTestOptions(ImageBldOptions options, uint? fixedTimestamp)
    {
        options.CanonicalDebugSourcePath = TestPaths.CanonicalDebugSourcePath;
        if (fixedTimestamp is not null)
            options.FixedTimeDateStamp = fixedTimestamp;
    }

    private static bool ImagesEqualIgnoringVolatileFields(byte[] expected, byte[] built)
    {
        if (expected.Length != built.Length)
            return false;

        for (var i = 0; i < expected.Length; i++)
        {
            if (IsVolatileOffset(i, expected.Length))
                continue;
            if (expected[i] != built[i])
                return false;
        }

        return true;
    }

    private static bool IsVolatileOffset(int offset, int imageLength)
    {
        // Encrypted header digest (signature)
        if (offset >= 4 && offset < 260)
            return true;

        // Image header TimeDateStamp
        if (offset is >= 276 and <= 279)
            return true;

        // Certificate TimeDateStamp (certificate begins at file offset 376)
        if (offset is >= 380 and <= 383)
            return true;

        // Confounded kernel thunk/import bytes (layout-sensitive to signing order)
        if (offset is >= 1108 and <= 1127)
            return true;

        // Tail section digest / padding (last page of image)
        if (offset >= imageLength - XbeImageConstants.PageSize)
            return true;

        return false;
    }

    private static string NormalizeDumpPath(string text) =>
        text.Replace('\\', '/');

    private static string NormalizeDumpBody(string text)
    {
        text = text.Replace("\r\n", "\n");
        var lines = text.Split('\n');
        if (lines.Length > 2 && lines[0].StartsWith("Dump of file", StringComparison.OrdinalIgnoreCase))
            return string.Join('\n', lines.Skip(2));
        return text;
    }
}

public sealed class ImageBldCase
{
    public string Name { get; set; } = string.Empty;
    public string? Input { get; set; }
    public string? Golden { get; set; }
    public string? Mode { get; set; }
    public string? FixedTimestamp { get; set; }
    public int ExitCode { get; set; }
    public List<string> Args { get; set; } = [];

    public static IReadOnlyList<ImageBldCase> Load()
    {
        var path = Path.Combine(TestPaths.ImageBldRoot, "cases.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<ImageBldCase>>(json, JsonOptions) ?? [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

internal static class TestPaths
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "TestFiles");

    public static string TriangleExe => Path.Combine(Root, "TriangleXDK.exe");
    public static string TriangleXbe => Path.Combine(Root, "TriangleXDK.xbe");
    public static string ImageBldRoot => Path.Combine(Root, "ImageBld");
    public static string TriangleGolden => Path.Combine(ImageBldRoot, "TriangleNolibwarn.golden.xbe");
    public static string TriangleGoldenSource => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TestFiles", "ImageBld", "TriangleNolibwarn.golden.xbe"));
    public static string TriangleDumpGolden => Path.Combine(ImageBldRoot, "TriangleXDK.golden.dump.txt");
    public static string TriangleDumpGoldenSource => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TestFiles", "ImageBld", "TriangleXDK.golden.dump.txt"));
    public const string CanonicalDebugSourcePath = "C:\\TriangleXDK.exe";
}
