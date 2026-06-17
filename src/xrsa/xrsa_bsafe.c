/*
 * BSafe RSA public/private operations (Microsoft rsa32 API).
 */
#include <windows.h>
#include <rsa.h>
#include <rsa_math.h>
#include <string.h>
#include "xrsa_int.h"

static DWORD KeyPDWords(DWORD bitlen)
{
    DWORD half = bitlen >> 1;
    DWORD pd = (half >> 5) + 1;
    if (half & 31)
    {
        ++pd;
    }
    return pd;
}

BOOL RSA32API BSafeGetPrvKeyParts(LPBSAFE_PRV_KEY key, LPBSAFE_KEY_PARTS parts)
{
    BYTE *p;
    DWORD half;

    if (!key || !parts || key->magic != RSA2)
    {
        return FALSE;
    }

    p = (BYTE *)key + sizeof(BSAFE_PUB_KEY);
    parts->modulus = p;
    p += key->keylen;
    parts->prime1 = p;
    half = key->keylen >> 1;
    p += half;
    parts->prime2 = p;
    p += half;
    parts->exp1 = p;
    p += half;
    parts->exp2 = p;
    p += half;
    parts->coef = p;
    p += half;
    parts->prvexp = p;
    p += key->keylen;
    parts->invmod = p;
    p += key->keylen;
    parts->invpr1 = p;
    p += half;
    parts->invpr2 = p;
    return TRUE;
}

BYTE *RSA32API BSafeGetPubKeyModulus(LPBSAFE_PUB_KEY key)
{
    if (!key || key->magic != RSA1)
    {
        return NULL;
    }
    return (BYTE *)key + sizeof(BSAFE_PUB_KEY);
}

BOOL RSA32API BSafeEncPublic(
    const LPBSAFE_PUB_KEY key,
    cLPBYTE part_in,
    LPBYTE part_out)
{
    DWORD pdWords;
    DWORD cmpWords;
    DWORD expBuf[XRSA_MAX_WORDS];

    if (!key || key->magic != RSA1 || !part_in || !part_out)
    {
        return FALSE;
    }

    pdWords = KeyPDWords(key->bitlen);
    cmpWords = pdWords * 2;
    if (cmpWords > XRSA_MAX_WORDS)
    {
        return FALSE;
    }

    if (key->pubexp == 1)
    {
        memcpy(part_out, part_in, key->keylen);
        return TRUE;
    }

    if (Compare((LPDWORD)part_in, (LPDWORD)BSafeGetPubKeyModulus((LPBSAFE_PUB_KEY)key), cmpWords) >= 0)
    {
        return FALSE;
    }

    SetValDWORD(expBuf, key->pubexp, cmpWords);
    if (!BenalohModExp((LPDWORD)part_out,
                       (LPDWORD)part_in,
                       expBuf,
                       (LPDWORD)BSafeGetPubKeyModulus((LPBSAFE_PUB_KEY)key),
                       cmpWords))
    {
        return FALSE;
    }

    return TRUE;
}

BOOL RSA32API BSafeDecPrivate(
    const LPBSAFE_PRV_KEY key,
    cLPBYTE part_in,
    LPBYTE part_out)
{
    BSAFE_KEY_PARTS parts;
    DWORD pdWords;

    if (!key || key->magic != RSA2 || !part_in || !part_out)
    {
        return FALSE;
    }
    if (!BSafeGetPrvKeyParts((LPBSAFE_PRV_KEY)key, &parts))
    {
        return FALSE;
    }

    pdWords = KeyPDWords(key->bitlen);
    if (pdWords > XRSA_MAX_WORDS)
    {
        return FALSE;
    }

    if (key->pubexp == 1)
    {
        memcpy(part_out, part_in, key->keylen);
        return TRUE;
    }

    return BenalohModRoot((LPDWORD)part_out,
                          (LPDWORD)part_in,
                          (LPDWORD)parts.prime1,
                          (LPDWORD)parts.prime2,
                          (LPDWORD)parts.exp1,
                          (LPDWORD)parts.exp2,
                          (LPDWORD)parts.coef,
                          pdWords);
}

BOOL RSA32API BSafeComputePDWords(LPDWORD bits, LPDWORD pdwords)
{
    DWORD value;
    DWORD pd;

    value = *bits;
    if ((value & 1) || value < 32)
    {
        return FALSE;
    }
    value >>= 1;
    *bits = value;
    pd = (value >> 5) + 1;
    if (value & 31)
    {
        ++pd;
    }
    *pdwords = pd;
    return TRUE;
}

BOOL RSA32API BSafeComputeKeySizes(
    LPDWORD PublicKeySize,
    LPDWORD PrivateKeySize,
    LPDWORD bits)
{
    DWORD value;
    DWORD pd;

    value = *bits;
    if ((value & 1) || value < 32)
    {
        return FALSE;
    }
    value >>= 1;
    *bits = value;
    pd = (value >> 5) + 1;
    if (value & 31)
    {
        ++pd;
    }
    *PrivateKeySize = pd * 40 + sizeof(BSAFE_PRV_KEY);
    *PublicKeySize = pd * 8 + sizeof(BSAFE_PUB_KEY);
    return TRUE;
}

void RSA32API BSafeFreePubKey(LPBSAFE_PUB_KEY public_key)
{
    if (public_key && public_key->magic == RSA1)
    {
        LocalFree((HLOCAL)public_key);
    }
}

void RSA32API BSafeFreePrvKey(LPBSAFE_PRV_KEY private_key)
{
    if (private_key && private_key->magic == RSA2)
    {
        LocalFree((HLOCAL)private_key);
    }
}
