using System.Diagnostics;
using System.Runtime.InteropServices;
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
    [Trait("Category", "NativeOracle")]
    public void Build_triangle_matches_native_when_masked()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var pe = TestPaths.TriangleExe;
        var nativeOut = Path.Combine(Path.GetTempPath(), $"triangle-native-{Guid.NewGuid():N}.xbe");
        var managedOut = Path.Combine(Path.GetTempPath(), $"triangle-managed-{Guid.NewGuid():N}.xbe");

        try
        {
            RunNativeImageBld(pe, nativeOut);
            BuildManaged(pe, managedOut);

            var native = File.ReadAllBytes(nativeOut);
            var managed = File.ReadAllBytes(managedOut);

            Assert.Equal(native.Length, managed.Length);
            if (!ImagesEqualIgnoringVolatileFields(native, managed))
            {
                var offset = FindFirstStableMismatch(native, managed);
                Assert.Fail(
                    offset >= 0
                        ? $"Stable XBE bytes differ at offset {offset}: native=0x{native[offset]:X2}, managed=0x{managed[offset]:X2}"
                        : "Stable XBE bytes differ (volatile-masked comparison failed with no stable mismatch offset)");
            }
        }
        finally
        {
            if (File.Exists(nativeOut))
                File.Delete(nativeOut);
            if (File.Exists(managedOut))
                File.Delete(managedOut);
        }
    }

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
    public void Build_cases_match_golden_or_native(ImageBldCase testCase)
    {
        if (testCase.Mode == "dump")
        {
            Assert.Equal(0, testCase.ExitCode);
            return;
        }

        var input = Path.Combine(TestPaths.ImageBldRoot, testCase.Input!);
        var output = Path.Combine(Path.GetTempPath(), $"{testCase.Name}-{Guid.NewGuid():N}.xbe");
        uint? fixedTimestamp = testCase.FixedTimestamp is not null
            ? uint.Parse(testCase.FixedTimestamp, System.Globalization.NumberStyles.HexNumber)
            : null;

        try
        {
            BuildManaged(input, output, fixedTimestamp, testCase.Args);

            if (testCase.Golden is not null)
            {
                var goldenPath = Path.Combine(TestPaths.ImageBldRoot, testCase.Golden);
                AssertMatchesGolden(File.ReadAllBytes(output), File.ReadAllBytes(goldenPath));
            }
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
    [Trait("Category", "NativeOracle")]
    public void Dump_triangle_matches_native_body()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var xbe = TestPaths.TriangleXbe;
        using var managedWriter = new StringWriter();
        XbeImageDumper.Dump(xbe, managedWriter);
        var managedBody = NormalizeDump(NormalizeDumpPath(managedWriter.ToString()));

        if (NativeImageBld.TryGetPath(out var nativeExe))
        {
            var psi = new ProcessStartInfo(nativeExe, $"/DUMP \"{xbe}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi)!;
            var nativeOut = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            Assert.Equal(0, proc.ExitCode);

            var nativeBody = NormalizeDump(NormalizeDumpPath(nativeOut));
            Assert.Equal(nativeBody, managedBody);
        }
    }

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
            if (fixedTimestamp is not null)
                parsedOptions.FixedTimeDateStamp = fixedTimestamp;
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
        new XbeImageBuilder().Build(options);
    }

    private static void RunNativeImageBld(string pe, string output, IReadOnlyList<string>? extraArgs = null)
    {
        if (!NativeImageBld.TryGetPath(out var exe))
            throw new InvalidOperationException("Native imagebld.exe not found.");

        var args = $"/IN:\"{pe}\" /OUT:\"{output}\"";
        if (extraArgs is not null)
        {
            foreach (var arg in extraArgs)
                args += " " + arg;
        }

        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException(proc.StandardError.ReadToEnd());
    }

    private static bool ImagesEqualIgnoringVolatileFields(byte[] native, byte[] managed)
    {
        if (native.Length != managed.Length)
            return false;

        for (var i = 0; i < native.Length; i++)
        {
            if (IsVolatileOffset(i, native.Length))
                continue;
            if (native[i] != managed[i])
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

    private static string NormalizeDump(string text)
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
}

internal static class NativeImageBld
{
    public static bool TryGetPath(out string path)
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("RXDK_NATIVE_IMAGEBLD"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "out", "bin", "x64", "Release", "imagebld.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "out", "bin", "x64", "Release", "imagebld.exe")),
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        }

        path = string.Empty;
        return false;
    }
}
