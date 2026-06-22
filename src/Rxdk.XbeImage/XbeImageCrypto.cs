using Rxdk.XbeImage.Internal;

namespace Rxdk.XbeImage;

public static class XbeImageCrypto
{
    public static void CalcDigest(ReadOnlySpan<byte> message, Span<byte> digest) =>
        XbeSha1.CalcDigest(message, digest);

    public static void CalcDigestRaw(ReadOnlySpan<byte> message, Span<byte> digest) =>
        XbeSha1.CalcDigestRaw(message, digest);

    public static void SignDigest(ReadOnlySpan<byte> digest, ReadOnlySpan<byte> privateKey, Span<byte> signature) =>
        XbeRsaBsafe.SignDigest(digest, privateKey, signature);

    public static void SignImageHeaders(
        ReadOnlySpan<byte> headerBytes,
        int digestStartOffset,
        ReadOnlySpan<byte> privateKey,
        Span<byte> encryptedDigest)
    {
        Span<byte> digest = stackalloc byte[XbeImageConstants.DigestLength];
        var digestLength = headerBytes.Length - digestStartOffset;
        CalcDigest(headerBytes.Slice(digestStartOffset, digestLength), digest);
        SignDigest(digest, privateKey, encryptedDigest);
    }
}
