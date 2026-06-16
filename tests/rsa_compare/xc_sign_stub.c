#include "test_common.h"
#include <stdlib.h>
#include <string.h>

static PBYTE shaEncodings[] = {
    (PBYTE)"\x0f\x14\x04\x00\x05\x1a\x02\x03\x0e\x2b\x05\x06\x09\x30\x21\x30",
    (PBYTE)"\x0d\x14\x04\x1a\x02\x03\x0e\x2b\x05\x06\x07\x30\x1f\x30",
    (PBYTE)"\x00"
};

static void XCApplyPKCS1SigningFmt(PBYTE pbKey, PBYTE pbDigest, PBYTE pbPKCS1Format)
{
    PBYTE pbStart;
    PBYTE pbEnd;
    BYTE bTmp;
    DWORD i;
    LPBSAFE_PUB_KEY pPubKey = (LPBSAFE_PUB_KEY)pbKey;

    pbPKCS1Format[pPubKey->datalen - 1] = 0x01;
    memset(pbPKCS1Format, 0xff, pPubKey->datalen - 1);
    for (i = 0; i < A_SHA_DIGEST_LEN; i++) {
        pbPKCS1Format[i] = pbDigest[A_SHA_DIGEST_LEN - (i + 1)];
    }
    pbEnd = (PBYTE)shaEncodings[0];
    pbStart = pbPKCS1Format + A_SHA_DIGEST_LEN;
    bTmp = *pbEnd++;
    while (0 < bTmp--) {
        *pbStart++ = *pbEnd++;
    }
    *pbStart++ = 0;
}

DWORD XCCalcSigSize(PBYTE pbPrivateKey)
{
    LPBSAFE_PRV_KEY pPrvKey = (LPBSAFE_PRV_KEY)pbPrivateKey;
    return (pPrvKey->bitlen + 7) / 8;
}

BOOLEAN XCSignDigest(PBYTE pbDigest, PBYTE pbPrivateKey, PBYTE pbSig)
{
    LPBSAFE_PRV_KEY pPrvKey = (LPBSAFE_PRV_KEY)pbPrivateKey;
    PBYTE pbInput;
    PBYTE pbOutput;
    DWORD dwSigLen;
    BOOLEAN ok;

    dwSigLen = (pPrvKey->bitlen + 7) / 8;
    pbInput = (PBYTE)LocalAlloc(LMEM_FIXED, pPrvKey->keylen);
    pbOutput = (PBYTE)LocalAlloc(LMEM_FIXED, pPrvKey->keylen);
    if (!pbInput || !pbOutput) {
        LocalFree(pbInput);
        LocalFree(pbOutput);
        return FALSE;
    }

    memset(pbInput, 0, pPrvKey->keylen);
    memset(pbOutput, 0, pPrvKey->keylen);
    XCApplyPKCS1SigningFmt(pbPrivateKey, pbDigest, pbInput);
    ok = BSafeDecPrivate(pPrvKey, pbInput, pbOutput);
    if (ok) {
        memcpy(pbSig, pbOutput, dwSigLen);
    }

    LocalFree(pbOutput);
    LocalFree(pbInput);
    return ok;
}
