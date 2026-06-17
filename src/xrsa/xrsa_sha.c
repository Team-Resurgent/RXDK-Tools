/*
 * Microsoft A_SHA API (SHA-1), source implementation matching rsa32.lib.
 * Padding/transform based on FIPS 180-1 (XKSHA1-style block indexing).
 */
#include <windows.h>
#include <sha.h>
#include <string.h>
#include "xrsa_int.h"

#define SHA1_BLOCK 64

static void SHA1Transform(DWORD state[5], const unsigned char block[SHA1_BLOCK])
{
    DWORD w[80];
    DWORD a, b, c, d, e;
    int i;

    for (i = 0; i < 16; ++i)
    {
        w[i] = ((DWORD)block[i * 4] << 24) |
               ((DWORD)block[i * 4 + 1] << 16) |
               ((DWORD)block[i * 4 + 2] << 8) |
               (DWORD)block[i * 4 + 3];
    }
    for (i = 16; i < 80; ++i)
    {
        w[i] = (w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16]);
        w[i] = (w[i] << 1) | (w[i] >> 31);
    }

    a = state[0];
    b = state[1];
    c = state[2];
    d = state[3];
    e = state[4];

#define ROL(v, s) (((v) << (s)) | ((v) >> (32 - (s))))
    for (i = 0; i < 20; ++i)
    {
        DWORD t = ROL(a, 5) + ((b & c) | ((~b) & d)) + e + w[i] + 0x5A827999;
        e = d;
        d = c;
        c = ROL(b, 30);
        b = a;
        a = t;
    }
    for (i = 20; i < 40; ++i)
    {
        DWORD t = ROL(a, 5) + (b ^ c ^ d) + e + w[i] + 0x6ED9EBA1;
        e = d;
        d = c;
        c = ROL(b, 30);
        b = a;
        a = t;
    }
    for (i = 40; i < 60; ++i)
    {
        DWORD t = ROL(a, 5) + ((b & c) | (b & d) | (c & d)) + e + w[i] + 0x8F1BBCDC;
        e = d;
        d = c;
        c = ROL(b, 30);
        b = a;
        a = t;
    }
    for (i = 60; i < 80; ++i)
    {
        DWORD t = ROL(a, 5) + (b ^ c ^ d) + e + w[i] + 0xCA62C1D6;
        e = d;
        d = c;
        c = ROL(b, 30);
        b = a;
        a = t;
    }
#undef ROL

    state[0] += a;
    state[1] += b;
    state[2] += c;
    state[3] += d;
    state[4] += e;
}

static unsigned int SHA1BlockIndex(const A_SHA_CTX *ctx)
{
    return (ctx->count[0] >> 3) & 63;
}

static void SHA1ProcessBlock(A_SHA_CTX *ctx)
{
    SHA1Transform(ctx->state, ctx->buffer);
}

static void SHA1Pad(A_SHA_CTX *ctx)
{
    unsigned int idx;

    idx = SHA1BlockIndex(ctx);
    ctx->buffer[idx++] = 0x80;
    if (idx > 56)
    {
        while (idx < 64)
        {
            ctx->buffer[idx++] = 0;
        }
        SHA1ProcessBlock(ctx);
        idx = 0;
    }
    while (idx < 56)
    {
        ctx->buffer[idx++] = 0;
    }

    ctx->buffer[56] = (unsigned char)(ctx->count[1] >> 24);
    ctx->buffer[57] = (unsigned char)(ctx->count[1] >> 16);
    ctx->buffer[58] = (unsigned char)(ctx->count[1] >> 8);
    ctx->buffer[59] = (unsigned char)(ctx->count[1]);
    ctx->buffer[60] = (unsigned char)(ctx->count[0] >> 24);
    ctx->buffer[61] = (unsigned char)(ctx->count[0] >> 16);
    ctx->buffer[62] = (unsigned char)(ctx->count[0] >> 8);
    ctx->buffer[63] = (unsigned char)(ctx->count[0]);
    SHA1ProcessBlock(ctx);
}

void RSA32API A_SHAInit(A_SHA_CTX *ctx)
{
    ctx->state[0] = 0x67452301;
    ctx->state[1] = 0xEFCDAB89;
    ctx->state[2] = 0x98BADCFE;
    ctx->state[3] = 0x10325476;
    ctx->state[4] = 0xC3D2E1F0;
    ctx->count[0] = ctx->count[1] = 0;
    ctx->FinishFlag = 0;
}

void RSA32API A_SHAUpdate(A_SHA_CTX *ctx, unsigned char *data, unsigned int len)
{
    unsigned int i;
    unsigned int idx;

    if (ctx->FinishFlag)
    {
        return;
    }

    idx = SHA1BlockIndex(ctx);
    if ((ctx->count[0] += len << 3) < (len << 3))
    {
        ++ctx->count[1];
    }
    ctx->count[1] += (len >> 29);

    if (idx + len > 63)
    {
        i = 64 - idx;
        memcpy(&ctx->buffer[idx], data, i);
        SHA1ProcessBlock(ctx);
        for (; i + 63 < len; i += 64)
        {
            SHA1Transform(ctx->state, &data[i]);
        }
        idx = 0;
    }
    else
    {
        i = 0;
    }
    memcpy(&ctx->buffer[idx], &data[i], len - i);
}

void RSA32API A_SHAFinal(A_SHA_CTX *ctx, unsigned char digest[A_SHA_DIGEST_LEN])
{
    unsigned int i;

    SHA1Pad(ctx);
    for (i = 0; i < 20; ++i)
    {
        digest[i] = (unsigned char)((ctx->state[i >> 2] >> ((3 - (i & 3)) * 8)) & 255);
    }
    ctx->FinishFlag = 1;
}

void RSA32API A_SHAUpdateNS(A_SHA_CTX *ctx, unsigned char *data, unsigned int len)
{
    A_SHAUpdate(ctx, data, len);
}

void RSA32API A_SHAFinalNS(A_SHA_CTX *ctx, unsigned char digest[A_SHA_DIGEST_LEN])
{
    SHA1Pad(ctx);
    DWORDToBigEndian(digest, ctx->state, 5);
    ctx->FinishFlag = 1;
}
