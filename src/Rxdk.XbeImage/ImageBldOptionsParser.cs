using System.Text;

namespace Rxdk.XbeImage;

public static class ImageBldOptionsParser
{
    public static ImageBldOptions Parse(string[] args) =>
        Parse(args, expandLegacyArgv: false);

    public static ImageBldOptions Parse(string[] args, bool expandLegacyArgv)
    {
        var effectiveArgs = expandLegacyArgv ? ImageBldLegacyArgv.Expand(args) : args;
        return ParseCore(effectiveArgs);
    }

    private static ImageBldOptions ParseCore(string[] args)
    {
        if (args.Length == 0)
        {
            return new ImageBldOptions { ShowUsage = true };
        }

        var options = new ImageBldOptions();
        var index = 0;

        if (IsSwitch(args[0]))
        {
            var switchBody = args[0][1..];
            if (string.Equals(switchBody, "DUMP", StringComparison.OrdinalIgnoreCase))
            {
                options.Mode = ImageBldParseMode.Dump;
                index = 1;
                ParseDumpArguments(options, args, ref index);
                return options;
            }
        }

        while (index < args.Length)
        {
            var arg = args[index++];
            if (arg.Length > 0 && arg[0] == '@')
            {
                ProcessCommandFile(options, arg[1..]);
            }
            else
            {
                ProcessCommandOption(options, arg);
            }
        }

        PostProcessInitFlags(options);
        PostProcessInsertFiles(options);
        return options;
    }

    private static void ParseDumpArguments(ImageBldOptions options, string[] args, ref int index)
    {
        while (index < args.Length)
        {
            var arg = args[index++];

            if (IsSwitch(arg))
            {
                var switchBody = arg[1..];
                if (string.Equals(switchBody, "?", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(switchBody, "HELP", StringComparison.OrdinalIgnoreCase))
                {
                    options.ShowUsage = true;
                    return;
                }

                throw new ImageBldParseException($"Unrecognized option {arg}.");
            }

            options.InputFilePath ??= arg;
        }

        if (options.InputFilePath is null)
        {
            options.ShowUsage = true;
        }
    }

    private static void PostProcessInitFlags(ImageBldOptions options)
    {
        if (options.LimitMemory)
        {
            options.InitFlags |= XbeImageConstants.XinitLimitDevkitMemory;
        }

        if (options.NoSetupHardDisk)
        {
            options.InitFlags |= XbeImageConstants.XinitNoSetupHardDisk;
        }

        if (options.DontModifyHardDisk)
        {
            options.InitFlags |= XbeImageConstants.XinitDontModifyHardDisk;
        }

        if (options.FormatUtilityDrive)
        {
            options.InitFlags |= XbeImageConstants.XinitFormatUtilityDrive;
        }

        if (options.DontMountUtilityDrive)
        {
            options.InitFlags &= ~XbeImageConstants.XinitMountUtilityDrive;
        }

        switch (options.UtilityDriveClusterSize)
        {
            case 16384:
                options.InitFlags &= ~XbeImageConstants.XinitUtilityDriveClusterSizeMask;
                options.InitFlags |= XbeImageConstants.XinitUtilityDrive16KClusterSize;
                break;

            case 32768:
                options.InitFlags &= ~XbeImageConstants.XinitUtilityDriveClusterSizeMask;
                options.InitFlags |= XbeImageConstants.XinitUtilityDrive32KClusterSize;
                break;

            case 65536:
                options.InitFlags &= ~XbeImageConstants.XinitUtilityDriveClusterSizeMask;
                options.InitFlags |= XbeImageConstants.XinitUtilityDrive64KClusterSize;
                break;
        }
    }

    private static void PostProcessInsertFiles(ImageBldOptions options)
    {
        if (options.DefaultSaveImage is not null)
        {
            options.InsertFiles.Insert(0, new ImageBldInsertFileEntry
            {
                FilePath = options.DefaultSaveImage,
                SectionName = "$$XSIMAGE",
                NoPreload = true,
                ReadOnly = true,
            });
        }

        if (options.TitleImage is not null)
        {
            options.InsertFiles.Insert(0, new ImageBldInsertFileEntry
            {
                FilePath = options.TitleImage,
                SectionName = "$$XTIMAGE",
                NoPreload = true,
                ReadOnly = true,
            });
        }

        if (options.TitleInfo is not null)
        {
            options.InsertFiles.Insert(0, new ImageBldInsertFileEntry
            {
                FilePath = options.TitleInfo,
                SectionName = "$$XTINFO",
                NoPreload = true,
                ReadOnly = true,
            });
        }
    }

    private static void ProcessCommandFile(ImageBldOptions options, string commandFilePath)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(commandFilePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            throw new ImageBldParseException($"Cannot open input file {commandFilePath}.");
        }

        foreach (var line in lines)
        {
            foreach (var token in TokenizeResponseLine(line))
            {
                ProcessCommandOption(options, token);
            }
        }
    }

    private static IEnumerable<string> TokenizeResponseLine(string line)
    {
        var index = 0;

        while (index < line.Length)
        {
            while (index < line.Length && char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            if (index >= line.Length)
            {
                yield break;
            }

            var builder = new StringBuilder();

            while (index < line.Length && !char.IsWhiteSpace(line[index]))
            {
                if (line[index] == '"')
                {
                    index++;
                    while (index < line.Length && line[index] != '"' && line[index] != '\n')
                    {
                        builder.Append(line[index++]);
                    }

                    if (index < line.Length && line[index] == '"')
                    {
                        index++;
                    }
                }
                else
                {
                    builder.Append(line[index++]);
                }
            }

            if (index < line.Length)
            {
                index++;
            }

            if (builder.Length > 0)
            {
                yield return builder.ToString();
            }
        }
    }

    private static void ProcessCommandOption(ImageBldOptions options, string commandOption)
    {
        if (commandOption.Length == 0 || (commandOption[0] != '-' && commandOption[0] != '/'))
        {
            options.InputFilePath = commandOption;
            return;
        }

        var switchBody = commandOption[1..];

        if (TryMatchStringValue(switchBody, "IN", out var stringValue))
        {
            options.InputFilePath = stringValue;
            return;
        }

        if (TryMatchStringValue(switchBody, "OUT", out stringValue))
        {
            options.OutputFilePath = stringValue;
            return;
        }

        if (TryMatchStringValue(switchBody, "NOPRELOAD", out stringValue))
        {
            options.NoPreloadSections.Add(new ImageBldNoPreloadEntry { SectionName = stringValue });
            return;
        }

        if (TryMatchIntegerValue(switchBody, "STACK", out var integerValue))
        {
            options.SizeOfStack = integerValue;
            return;
        }

        if (TryMatchIntegerValue(switchBody, "INITFLAGS", out integerValue))
        {
            options.InitFlags = integerValue;
            return;
        }

        if (TryMatchIntegerValue(switchBody, "VERSION", out integerValue))
        {
            options.Version = integerValue;
            return;
        }

        if (TryMatchIntegerValue(switchBody, "TESTVERSION", out integerValue))
        {
            options.Version = integerValue;
            return;
        }

        if (TryMatchIntegerValue(switchBody, "TESTREGION", out integerValue))
        {
            options.TestGameRegion = integerValue;
            return;
        }

        if (TryMatchIntegerValue(switchBody, "TESTMEDIATYPES", out integerValue))
        {
            options.TestAllowedMediaTypes = integerValue;
            return;
        }

        if (TryMatchIntegerValue(switchBody, "TESTRATINGS", out integerValue))
        {
            options.TestGameRatings = integerValue;
            return;
        }

        if (TryMatchIntegerValue(switchBody, "TESTID", out integerValue))
        {
            options.TestTitleId = integerValue;
            return;
        }

        if (TryMatchStringValue(switchBody, "TESTALTID", out stringValue))
        {
            ProcessTestAltId(options, stringValue);
            return;
        }

        if (TryMatchStringValue(switchBody, "TESTNAME", out stringValue))
        {
            options.TestTitleName = stringValue;
            return;
        }

        if (TryMatchCertificateKey(switchBody, "TESTLANKEY", out var certificateKey))
        {
            options.TestLanKey = certificateKey;
            return;
        }

        if (TryMatchCertificateKey(switchBody, "TESTSIGNKEY", out certificateKey))
        {
            options.TestSignatureKey = certificateKey;
            return;
        }

        if (TryMatchStringValue(switchBody, "TITLEIMAGE", out stringValue))
        {
            options.TitleImage = stringValue;
            return;
        }

        if (TryMatchStringValue(switchBody, "TITLEINFO", out stringValue))
        {
            options.TitleInfo = stringValue;
            return;
        }

        if (TryMatchStringValue(switchBody, "DEFAULTSAVEIMAGE", out stringValue))
        {
            options.DefaultSaveImage = stringValue;
            return;
        }

        if (TryMatchStringValue(switchBody, "INSERTFILE", out stringValue))
        {
            ProcessInsertFile(options, stringValue);
            return;
        }

        if (string.Equals(switchBody, "NOLIBWARN", StringComparison.OrdinalIgnoreCase))
        {
            options.NoWarnLibraryApproval = true;
            return;
        }

        if (switchBody.StartsWith("PEHEADER", StringComparison.OrdinalIgnoreCase))
        {
            options.EmitPeHeader = true;
            return;
        }

        if (string.Equals(switchBody, "LIMITMEM", StringComparison.OrdinalIgnoreCase))
        {
            options.LimitMemory = true;
            return;
        }

        if (string.Equals(switchBody, "NOSETUPHD", StringComparison.OrdinalIgnoreCase))
        {
            options.NoSetupHardDisk = true;
            return;
        }

        if (string.Equals(switchBody, "DONTMODIFYHD", StringComparison.OrdinalIgnoreCase))
        {
            options.DontModifyHardDisk = true;
            return;
        }

        if (string.Equals(switchBody, "DONTMOUNTUD", StringComparison.OrdinalIgnoreCase))
        {
            if (options.FormatUtilityDrive)
            {
                throw new ImageBldParseException("Cannot specify /FORMATUD and /DONTMOUNTUD together.");
            }

            options.DontMountUtilityDrive = true;
            return;
        }

        if (string.Equals(switchBody, "FORMATUD", StringComparison.OrdinalIgnoreCase))
        {
            if (options.DontMountUtilityDrive)
            {
                throw new ImageBldParseException("Cannot specify /FORMATUD and /DONTMOUNTUD together.");
            }

            options.FormatUtilityDrive = true;
            return;
        }

        if (TryMatchIntegerValue(switchBody, "UDCLUSTER", out integerValue))
        {
            options.UtilityDriveClusterSize = NormalizeUtilityDriveClusterSize(integerValue);
            return;
        }

        if (string.Equals(switchBody, "NOLOGO", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(switchBody, "DEBUG", StringComparison.OrdinalIgnoreCase))
        {
            options.EmitPeHeader = true;
            return;
        }

        if (string.Equals(switchBody, "?", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(switchBody, "HELP", StringComparison.OrdinalIgnoreCase))
        {
            options.ShowUsage = true;
            return;
        }

        throw new ImageBldParseException($"Unrecognized option {switchBody}.");
    }

    private static void ProcessTestAltId(ImageBldOptions options, string value)
    {
        if (options.TestAlternateTitleIds.Count >= XbeImageConstants.AlternateTitleIdCount)
        {
            throw new ImageBldParseException("Too many /TESTALTID options were specified.");
        }

        var parts = value.Split(',', StringSplitOptions.None);
        if (parts.Length == 0 || parts[0].Length == 0)
        {
            throw new ImageBldParseException("Invalid number for option TESTALTID.");
        }

        ParseInteger("TESTALTID", parts[0], out var titleId);

        byte[]? signatureKey = null;
        if (parts.Length > 1 && parts[1].Length > 0)
        {
            signatureKey = ParseCertificateKey("TESTALTID", parts[1]);
        }

        if (parts.Length > 2 && parts[2].Length > 0)
        {
            throw new ImageBldParseException("Too many options for /INSERTFILE.");
        }

        options.TestAlternateTitleIds.Add(new ImageBldTestAlternateTitleId
        {
            TitleId = titleId,
            SignatureKey = signatureKey,
        });
    }

    private static void ProcessInsertFile(ImageBldOptions options, string value)
    {
        var parts = value.Split(',', StringSplitOptions.None);
        if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw new ImageBldParseException("Missing section name for /INSERTFILE.");
        }

        var entry = new ImageBldInsertFileEntry
        {
            FilePath = parts[0],
            SectionName = parts[1],
        };

        if (parts.Length > 2 && parts[2].Length > 0)
        {
            var readOnly = false;
            var noPreload = false;

            foreach (var attribute in parts[2])
            {
                switch (char.ToUpperInvariant(attribute))
                {
                    case 'R':
                        readOnly = true;
                        break;

                    case 'N':
                        noPreload = true;
                        break;

                    default:
                        throw new ImageBldParseException("Invalid option INSERTFILE.");
                }
            }

            if (parts.Length > 3 && parts[3].Length > 0)
            {
                throw new ImageBldParseException("Too many options for /INSERTFILE.");
            }

            entry = new ImageBldInsertFileEntry
            {
                FilePath = entry.FilePath,
                SectionName = entry.SectionName,
                NoPreload = noPreload,
                ReadOnly = readOnly,
            };
        }

        options.InsertFiles.Add(entry);
    }

    private static uint NormalizeUtilityDriveClusterSize(uint clusterSize) =>
        clusterSize switch
        {
            16 or 16384 => 16384,
            32 or 32768 => 32768,
            64 or 65536 => 65536,
            _ => throw new ImageBldParseException("Invalid utility drive cluster size."),
        };

    private static bool TryMatchStringValue(string commandOption, string matchString, out string stringValue)
    {
        stringValue = string.Empty;

        if (!commandOption.StartsWith(matchString, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (commandOption.Length == matchString.Length)
        {
            throw new ImageBldParseException($"Missing argument for option {matchString}.");
        }

        if (commandOption[matchString.Length] != ':')
        {
            return false;
        }

        if (commandOption.Length == matchString.Length + 1)
        {
            throw new ImageBldParseException($"Missing argument for option {matchString}.");
        }

        stringValue = commandOption[(matchString.Length + 1)..];
        return true;
    }

    private static bool TryMatchIntegerValue(string commandOption, string matchString, out uint integerValue)
    {
        if (!TryMatchStringValue(commandOption, matchString, out var stringValue))
        {
            integerValue = 0;
            return false;
        }

        ParseInteger(matchString, stringValue, out integerValue);
        return true;
    }

    private static bool TryMatchCertificateKey(string commandOption, string matchString, out byte[] certificateKey)
    {
        if (!TryMatchStringValue(commandOption, matchString, out var stringValue))
        {
            certificateKey = [];
            return false;
        }

        certificateKey = ParseCertificateKey(matchString, stringValue);
        return true;
    }

    private static void ParseInteger(string matchString, string integerText, out uint integerValue)
    {
        try
        {
            if (integerText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                var digits = integerText[2..];
                if (digits.Length == 0 || !digits.All(IsHexDigit))
                {
                    throw new FormatException();
                }

                integerValue = Convert.ToUInt32(digits, 16);
                return;
            }

            if (integerText.Length > 1 &&
                integerText[0] == '0' &&
                integerText.Skip(1).All(static c => c >= '0' && c <= '7'))
            {
                integerValue = Convert.ToUInt32(integerText, 8);
                return;
            }

            if (integerText.Length == 0 || !integerText.All(char.IsDigit))
            {
                throw new FormatException();
            }

            integerValue = Convert.ToUInt32(integerText, 10);
        }
        catch (OverflowException)
        {
            throw new ImageBldParseException($"Invalid number for option {matchString}.");
        }
        catch (FormatException)
        {
            throw new ImageBldParseException($"Invalid number for option {matchString}.");
        }
    }

    private static byte[] ParseCertificateKey(string matchString, string certificateKeyText)
    {
        if (certificateKeyText.Length != XbeImageConstants.CertificateKeyLength * 2)
        {
            throw new ImageBldParseException($"Invalid certificate key for option {matchString}.");
        }

        var key = new byte[XbeImageConstants.CertificateKeyLength];
        for (var i = 0; i < key.Length; i++)
        {
            var high = certificateKeyText[i * 2];
            var low = certificateKeyText[i * 2 + 1];
            if (!IsHexDigit(high) || !IsHexDigit(low))
            {
                throw new ImageBldParseException($"Invalid certificate key for option {matchString}.");
            }

            key[i] = (byte)((HexCharacterToInteger(high) << 4) | HexCharacterToInteger(low));
        }

        return key;
    }

    private static int HexCharacterToInteger(char character) =>
        char.IsDigit(character)
            ? character - '0'
            : char.ToUpperInvariant(character) - 'A' + 10;

    private static bool IsHexDigit(char character) =>
        char.IsDigit(character) ||
        (character >= 'a' && character <= 'f') ||
        (character >= 'A' && character <= 'F');

    private static bool IsSwitch(string arg) =>
        arg.Length > 0 && arg[0] is '-' or '/';

    public static bool IsNoPreloadSection(ImageBldOptions options, string sectionName) =>
        options.NoPreloadSections.Any(entry =>
            string.Equals(entry.SectionName, sectionName, StringComparison.OrdinalIgnoreCase));
}
