using Rxdk.Xbdm.Managed;
using Xunit;

namespace Rxdk.Xbdm.Tests;

/// <summary>
/// Golden-vector checks for the managed XBC/Benaloh port used by secure auth.
/// Vectors were captured from the managed implementation at import time;
/// cross-check against <c>src/xboxdbg/secure.c</c> when changing crypto code.
/// </summary>
public sealed class XbdmCryptoValidationTests
{
  // Captured via Rxdk.Xbdm.CryptoGen (2026-06-17).
  private const ulong CrossGolden = 0x7950961628E6EB99UL;
  private const ulong AuthPasswdGolden = 0xDDA39E0418767C5EUL;
  private const ulong AuthRespGolden = 0x18EAE02C10D8E1C8UL;
  private const ulong HashAsciiXboxGolden = 0x21957915DC3B5479UL;

  [Fact]
  public void XbcCross_matches_golden_vector()
  {
    var actual = XbcCrypto.Cross(0x0123456789ABCDEFUL, 0xFEDCBA9876543210UL);
    Assert.Equal(CrossGolden, actual);
  }

  [Fact]
  public void XbcCross_auth_chain_matches_golden_vectors()
  {
    const ulong seed = 0xA5A5A5A5A5A5A5A5UL;
    const ulong boxId = 0x1234567890ABCDEFUL;
    const ulong nonce = 0xCAFEBABEDEADBEEFUL;

    var passwd = XbcCrypto.Cross(seed, boxId);
    var resp = XbcCrypto.Cross(passwd, nonce);

    Assert.Equal(AuthPasswdGolden, passwd);
    Assert.Equal(AuthRespGolden, resp);
  }

  [Fact]
  public void XbcHashAscii_password_matches_golden_vector()
  {
    var hash = 0UL;
    XbcCrypto.HashDataAsciiPassword(ref hash, "xbox");
    Assert.Equal(HashAsciiXboxGolden, hash);
  }

  [Fact]
  public void XbcHashAscii_empty_password_is_zero()
  {
    var hash = 0UL;
    XbcCrypto.HashDataAsciiPassword(ref hash, string.Empty);
    Assert.Equal(0UL, hash);
  }

  [Fact]
  public void XbcCross_is_deterministic()
  {
    const ulong a = 0x1111222233334444UL;
    const ulong b = 0xAAAABBBBCCCCDDDDUL;
    Assert.Equal(XbcCrypto.Cross(a, b), XbcCrypto.Cross(a, b));
  }

  [Theory]
  [InlineData(3u, 4u, 11u, 4u)] // 3^4 mod 11
  [InlineData(5u, 1u, 7u, 5u)]  // 5^1 mod 7
  [InlineData(2u, 10u, 1000u, 24u)] // 2^10 mod 1000
  public void BenalohModExp_small_moduli(uint b, uint e, uint mod, uint expected)
  {
    var result = new uint[1];
    BenalohModExp.ModExp(result, [b], [e], [mod], 1);
    Assert.Equal(expected, result[0]);
  }
}
