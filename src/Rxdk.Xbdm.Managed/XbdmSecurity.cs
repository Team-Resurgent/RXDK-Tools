using System.Net.Sockets;
using System.Security;
using System.Security.Cryptography;
using Microsoft.Win32;
using Rxdk.Xbdm;

namespace Rxdk.Xbdm.Managed;

public sealed class XbdmConnectOptions
{
    public string? AdminPassword { get; init; }
    public bool Authenticate { get; init; } = true;
    public TimeSpan? ConnectTimeout { get; init; }
}

internal static class XbdmSecurity
{
    private static readonly byte[] OakleyGroup1Base =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02,
    ];

    private static readonly byte[] OakleyGroup1Mod =
    [
        0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
        0xc9, 0x0f, 0xda, 0xa2, 0x21, 0x68, 0xc2, 0x34,
        0xc4, 0xc6, 0x62, 0x8b, 0x80, 0xdc, 0x1c, 0xd1,
        0x29, 0x02, 0x4e, 0x08, 0x8a, 0x67, 0xcc, 0x74,
        0x02, 0x0b, 0xbe, 0xa6, 0x3b, 0x13, 0x9b, 0x22,
        0x51, 0x4a, 0x08, 0x79, 0x8e, 0x34, 0x04, 0xdd,
        0xef, 0x95, 0x19, 0xb3, 0xcd, 0x3a, 0x43, 0x1b,
        0x30, 0x2b, 0x0a, 0x6d, 0xf2, 0x5f, 0x14, 0x37,
        0x4f, 0xe1, 0x35, 0x6d, 0x6d, 0x51, 0xc2, 0x45,
        0xe4, 0x85, 0xb5, 0x76, 0x62, 0x5e, 0x7e, 0xc6,
        0xf4, 0x4c, 0x42, 0xe9, 0xa6, 0x3a, 0x36, 0x20,
        0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    ];

    private const int DhLen = 24;

    internal static void AuthenticateOnConnect(XbdmProtocolSession session, string welcomeLine, XbdmConnectOptions options, Socket socket)
    {
        if (!options.Authenticate)
            return;

        if (!XbdmProtocol.TryGetQwordParam(welcomeLine, "BOXID", out var boxId) ||
            !XbdmProtocol.TryGetQwordParam(welcomeLine, "NONCE", out var connectNonce))
        {
            return;
        }

        if (!string.IsNullOrEmpty(options.AdminPassword))
        {
            AuthenticateAdmin(session, connectNonce, options.AdminPassword);
            return;
        }

        AuthenticateUser(session, boxId, connectNonce, socket);
    }

    internal static void AuthenticateAdmin(XbdmProtocolSession session, ulong connectNonce, string password)
    {
        var passwd = 0UL;
        XbcCrypto.HashDataAsciiPassword(ref passwd, password);
        var response = XbcCrypto.Cross(passwd, connectNonce);
        var command = $"AUTHUSER ADMIN RESP=0q{response >> 32:x8}{response & 0xFFFFFFFF:x8}";
        var (hr, line) = session.SendCommandRaw(command);
        if (!XbdmProtocol.IsCommandSuccess(hr))
            throw XbdmException.FromHResult("Secure admin authentication failed.", hr, line);
    }

    internal static void AuthenticateUser(XbdmProtocolSession session, ulong boxId, ulong connectNonce, Socket socket)
    {
        var computerName = Environment.MachineName;
        if (computerName.Length > 31)
            computerName = computerName[..31];

        var seed = LoadOrCreateSecuritySeed(socket);
        var passwd = XbcCrypto.Cross(seed, boxId);
        var response = XbcCrypto.Cross(passwd, connectNonce);

        var command =
            $"AUTHUSER NAME=\"{computerName}\" RESP=0q{response >> 32:x8}{response & 0xFFFFFFFF:x8}";
        var (hr, line) = session.SendCommandRaw(command);
        if (hr == XbdmHResults.KeyExchange)
        {
            var dhKey = PerformKeyExchange(session);
            passwd ^= dhKey;
            command =
                $"AUTHUSER NAME=\"{computerName}\" PASSWD=0q{passwd >> 32:x8}{passwd & 0xFFFFFFFF:x8}";
            (hr, line) = session.SendCommandRaw(command);
        }

        if (!XbdmProtocol.IsCommandSuccess(hr))
            throw XbdmException.FromHResult("Secure user authentication failed.", hr, line);
    }

    internal static ulong PerformKeyExchange(XbdmProtocolSession session)
    {
        var (hr, _) = session.SendCommandRaw("KEYXCHG");
        if (hr != XbdmHResults.ReadyForBin)
            throw XbdmException.FromHResult("DH key exchange failed to start.", hr);

        var secret = GenerateDhSecret();
        var generator = ToUIntArray(OakleyGroup1Base);
        var modulus = ToUIntArray(OakleyGroup1Mod);
        var publicKey = new uint[DhLen];
        BenalohModExp.ModExp(publicKey, generator, secret, modulus, DhLen);

        session.SendBinary(ToBytes(publicKey));

        var statusLine = session.ReceiveLine();
        hr = XbdmProtocol.HResultFromStatusLine(statusLine);
        if (hr != XbdmHResults.Binresponse)
            throw XbdmException.FromHResult("Unexpected DH key exchange response.", hr, statusLine);

        var peerKeyBytes = new byte[96];
        session.ReceiveBinary(peerKeyBytes);
        var peerKey = ToUIntArray(peerKeyBytes);
        var shared = new uint[DhLen];
        BenalohModExp.ModExp(shared, peerKey, secret, modulus, DhLen);

        var sharedHash = 0UL;
        XbcCrypto.HashData(ref sharedHash, ToBytes(shared));
        return sharedHash;
    }

    internal static ulong GenerateLockBoxId()
    {
        Span<byte> buffer = stackalloc byte[48];
        var fileTime = BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc());
        fileTime.CopyTo(buffer[32..]);
        var boxId = 0UL;
        XbcCrypto.HashData(ref boxId, buffer);
        return boxId;
    }

    private static uint[] GenerateDhSecret()
    {
        var bytes = new byte[96];
        RandomNumberGenerator.Fill(bytes);
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 8), DateTime.UtcNow.ToFileTimeUtc());
        return ToUIntArray(bytes);
    }

    private static ulong LoadOrCreateSecuritySeed(Socket socket)
    {
        if (TryLoadSecuritySeed(out var seed))
            return seed;

        seed = CreateSecuritySeed(socket);
        SaveSecuritySeed(seed);
        return seed;
    }

    private static bool TryLoadSecuritySeed(out ulong seed)
    {
        seed = 0;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\XboxSDK");
                if (key?.GetValue("SecuritySeed") is byte[] bytes && bytes.Length >= 8)
                {
                    seed = BitConverter.ToUInt64(bytes, 0);
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (SecurityException)
            {
            }
        }

        var path = SecuritySeedPath();
        if (!File.Exists(path))
            return false;

        var fileBytes = File.ReadAllBytes(path);
        if (fileBytes.Length < 8)
            return false;

        seed = BitConverter.ToUInt64(fileBytes, 0);
        return true;
    }

    private static ulong CreateSecuritySeed(Socket socket)
    {
        Span<byte> seedBytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(seedBytes);
        var seed = BitConverter.ToUInt64(seedBytes);

        if (socket.LocalEndPoint is System.Net.IPEndPoint local)
            XbcCrypto.HashData(ref seed, local.Address.GetAddressBytes());

        return seed;
    }

    private static void SaveSecuritySeed(ulong seed)
    {
        var bytes = BitConverter.GetBytes(seed);
        var path = SecuritySeedPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\XboxSDK");
                key.SetValue("SecuritySeed", bytes, RegistryValueKind.Binary);
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (SecurityException)
            {
            }
        }
    }

    private static string SecuritySeedPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        const string folder = "Rxdk.XbNeighborhood";
        const string legacyFolder = "RXDKNeighborhood";
        var configDir = Path.Combine(appData, folder);
        var legacyDir = Path.Combine(appData, legacyFolder);
        if (!Directory.Exists(configDir) && Directory.Exists(legacyDir))
            configDir = legacyDir;

        return Path.Combine(configDir, "xbdm_security_seed.bin");
    }

    private static uint[] ToUIntArray(byte[] bytes)
    {
        var values = new uint[bytes.Length / 4];
        for (var i = 0; i < values.Length; i++)
            values[i] = BitConverter.ToUInt32(bytes, i * 4);
        return values;
    }

    private static uint[] ToUIntArray(ReadOnlySpan<byte> bytes)
    {
        var values = new uint[bytes.Length / 4];
        for (var i = 0; i < values.Length; i++)
            values[i] = BitConverter.ToUInt32(bytes.Slice(i * 4, 4));
        return values;
    }

    private static byte[] ToBytes(uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4, 4), values[i]);
        return bytes;
    }
}
