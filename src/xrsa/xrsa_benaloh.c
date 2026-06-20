/*
 * Benaloh modular exponentiation / CRT root (Microsoft rsa32 API).
 *
 * Source replacement for rsa32_benaloh.obj. The exported results
 * (A = B^C mod D, and the CRT modular root) are pure mathematical
 * values, so this uses the validated source bignum primitives
 * (Mod/Multiply/Square/Add/Sub/BitLen) via a standard left-to-right
 * binary exponentiation and Garner CRT recombination, matching the
 * reference outputs bit-for-bit.
 */
#include <windows.h>
#include <benaloh.h>
#include <rsa_math.h>
#include <rsa_fast.h>
#include <string.h>
#include "xrsa_int.h"

#define BENALOH_MAXW XRSA_MAX_WORDS
#define BENALOH_MAXW2 (2 * XRSA_MAX_WORDS)

static __inline DWORD BitAt(LPDWORD value, DWORD index)
{
    return (value[index >> 5] >> (index & 31)) & 1u;
}

BOOL RSA32API BenalohModExp(LPDWORD A, LPDWORD B, LPDWORD C, LPDWORD D, DWORD len)
{
    DWORD result[BENALOH_MAXW];
    DWORD base[BENALOH_MAXW];
    DWORD prod[BENALOH_MAXW2];
    DWORD bits;
    int i;

    if (len == 0 || len > BENALOH_MAXW)
    {
        return FALSE;
    }

    bits = BitLen(C, len);
    if (bits == 0)
    {
        SetValDWORD(A, 1, len);
        return TRUE;
    }

    /* base = B mod D (B is already reduced for our callers, but be safe). */
    memcpy(prod, B, len * sizeof(DWORD));
    memset(prod + len, 0, len * sizeof(DWORD));
    if (!Mod(prod, D, base, 2 * len, len))
    {
        return FALSE;
    }

    SetValDWORD(result, 1, len);

    for (i = (int)bits - 1; i >= 0; --i)
    {
        Square(prod, result, len);
        if (!Mod(prod, D, result, 2 * len, len))
        {
            return FALSE;
        }
        if (BitAt(C, (DWORD)i))
        {
            Multiply(prod, result, base, len);
            if (!Mod(prod, D, result, 2 * len, len))
            {
                return FALSE;
            }
        }
    }

    memcpy(A, result, len * sizeof(DWORD));
    return TRUE;
}

BOOL RSA32API BenalohModRoot(LPDWORD M, LPDWORD C, LPDWORD PP, LPDWORD QQ,
                             LPDWORD DP, LPDWORD DQ, LPDWORD CR, DWORD PSize)
{
    DWORD cTmp[BENALOH_MAXW2];
    DWORD mP[BENALOH_MAXW];
    DWORD mQ[BENALOH_MAXW];
    DWORD diff[BENALOH_MAXW];
    DWORD h[BENALOH_MAXW];
    DWORD prod[BENALOH_MAXW2];
    DWORD full = 2 * PSize;

    if (PSize == 0 || full > BENALOH_MAXW2 || PSize > BENALOH_MAXW)
    {
        return FALSE;
    }

    /* mP = (C mod P)^DP mod P */
    memcpy(cTmp, C, full * sizeof(DWORD));
    if (!Mod(cTmp, PP, mP, full, PSize))
    {
        return FALSE;
    }
    if (!BenalohModExp(mP, mP, DP, PP, PSize))
    {
        return FALSE;
    }

    /* mQ = (C mod Q)^DQ mod Q */
    memcpy(cTmp, C, full * sizeof(DWORD));
    if (!Mod(cTmp, QQ, mQ, full, PSize))
    {
        return FALSE;
    }
    if (!BenalohModExp(mQ, mQ, DQ, QQ, PSize))
    {
        return FALSE;
    }

    /* diff = (mP - mQ) mod P */
    if (Sub(diff, mP, mQ, PSize))
    {
        while (!Add(diff, diff, PP, PSize))
        {
            /* keep adding P until the subtraction underflow is corrected */
        }
    }

    /* h = (diff * CR) mod P */
    Multiply(prod, diff, CR, PSize);
    if (!Mod(prod, PP, h, full, PSize))
    {
        return FALSE;
    }

    /* M = mQ + h * Q */
    Multiply(prod, h, QQ, PSize);
    memset(cTmp, 0, full * sizeof(DWORD));
    memcpy(cTmp, mQ, PSize * sizeof(DWORD));
    Add(M, prod, cTmp, full);

    return TRUE;
}


