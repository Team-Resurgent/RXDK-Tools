#include <windows.h>
#include <rsa_math.h>
#include <rsa_fast.h>
#include <string.h>

static void CopyWords(LPDWORD dst, LPDWORD src, DWORD bytes)
{
    DWORD dwords = bytes / sizeof(DWORD);
    DWORD rem = bytes & 3;

    if (dwords) {
        memcpy(dst, src, dwords * sizeof(DWORD));
    }
    if (rem) {
        memcpy((BYTE*)dst + dwords * sizeof(DWORD),
               (BYTE*)src + dwords * sizeof(DWORD),
               rem);
    }
}

static void ZeroWords(LPDWORD dst, DWORD bytes)
{
    memset(dst, 0, bytes);
}

static DWORD TrimLen(LPDWORD a, DWORD n)
{
    while (n > 0 && a[n - 1] == 0) {
        --n;
    }
    if (n == 0) {
        return 1;
    }
    return n;
}

BOOL Increment(LPDWORD A, DWORD N)
{
    DWORD i;

    if (N == 0) {
        return TRUE;
    }

    for (i = 0; i < N; ++i) {
        ++A[i];
        if (A[i] != 0) {
            return FALSE;
        }
    }
    return TRUE;
}

void SetValDWORD(LPDWORD num, DWORD val, DWORD len)
{
    BYTE fill;
    DWORD i;
    DWORD pattern;

    num[0] = val;
    fill = (val & DIGIT_HIBIT) ? (BYTE)0xFF : (BYTE)0x00;
    if (len <= 1) {
        return;
    }

    pattern = (DWORD)fill | ((DWORD)fill << 8) | ((DWORD)fill << 16) | ((DWORD)fill << 24);
    for (i = 1; i < len; ++i) {
        num[i] = pattern;
    }
}

void TwoPower(LPDWORD A, DWORD V, DWORD N)
{
    DWORD word;
    DWORD bit;

    ZeroWords(A, N * sizeof(DWORD));
    word = V >> 5;
    bit = 1u << (V & 31u);
    if (word < N) {
        A[word] = bit;
    }
}

DWORD DigitLen(LPDWORD A, DWORD N)
{
    return TrimLen(A, N);
}

DWORD BitLen(LPDWORD A, DWORD N)
{
    DWORD idx;
    DWORD top;

    if (N == 0) {
        return 0;
    }

    idx = N;
    do {
        --idx;
        top = A[idx];
        if (top != 0) {
            break;
        }
    } while (idx > 0);

    if (top == 0) {
        return 0;
    }

    {
        DWORD bits = (idx + 1) * 32;
        while ((top & DIGIT_HIBIT) == 0) {
            top <<= 1;
            --bits;
        }
        return bits;
    }
}

int Compare(LPDWORD A, LPDWORD B, DWORD N)
{
    DWORD i;

    if (N == 0) {
        return 0;
    }

    for (i = N - 1; i > 0; --i) {
        if (A[i] > B[i]) {
            return 1;
        }
        if (A[i] < B[i]) {
            return -1;
        }
    }
    if (A[0] > B[0]) {
        return 1;
    }
    if (A[0] < B[0]) {
        return -1;
    }
    return 0;
}

void MultiplyLow(LPDWORD A, LPDWORD B, LPDWORD C, DWORD N)
{
    DWORD bLen;
    DWORD i;

    if (N == 0) {
        return;
    }

    bLen = TrimLen(B, N);
    if (C[0] == 0) {
        ZeroWords(A, N * sizeof(DWORD));
        return;
    }
    if (C[0] == 1) {
        CopyWords(A, B, N * sizeof(DWORD));
        return;
    }

    ZeroWords(A, N * sizeof(DWORD));
    for (i = 0; i + bLen <= N; ++i) {
        A[i + bLen] = Accumulate(A + i, C[i], B, bLen);
    }
    for (; i < N; ++i) {
        Accumulate(A + i, C[i], B, N - i);
    }
}

void Multiply(LPDWORD A, LPDWORD B, LPDWORD C, DWORD N)
{
    DWORD cLen;
    DWORD i;

    ZeroWords(A, 2 * N * sizeof(DWORD));
    if (N == 0) {
        return;
    }

    cLen = TrimLen(C, N);
    for (i = 0; i < N; ++i) {
        A[i + cLen] = Accumulate(A + i, B[i], C, cLen);
    }
}

void Square(LPDWORD A, LPDWORD B, DWORD N)
{
    DWORD bLen;
    DWORD i;
    LPDWORD pB;
    LPDWORD pA;

    ZeroWords(A, 2 * N * sizeof(DWORD));
    if (N == 0) {
        return;
    }

    bLen = TrimLen(B, N);
    if (bLen > 1) {
        i = bLen - 1;
        pB = B;
        pA = A + 1;
        do {
            pA[i] = Accumulate(pA, pB[0], pB + 1, i);
            --i;
            ++pB;
            pA += 2;
        } while (i > 0);
    }

    Add(A, A, A, bLen * 2);
    AccumulateSquares(A, B, bLen);
}

DWORD EstimateQuotient(DWORD n0, DWORD n1, DWORD d0, DWORD d1)
{
    DWORD numHi = n0;
    DWORD numLo = n1;

    if (d1 == 0 || (LONG)d0 < 0) {
        if (numHi >= d0) {
            return 0xFFFFFFFFu;
        }
        return (DWORD)((((unsigned __int64)numHi << 32) | numLo) / d0);
    }

    if (numHi > d0 || (numHi == d0 && numLo >= d1)) {
        return 0xFFFFFFFFu;
    }

    {
        DWORD quotient = 0;
        DWORD bit = 0x80000000u;

        do {
            numHi = (numHi << 1) | (numLo >> 31);
            numLo <<= 1;

            if (numHi > d0 || (numHi == d0 && numLo >= d1)) {
                if (numLo < d1) {
                    --numHi;
                }
                numLo -= d1;
                numHi -= d0;
                quotient |= bit;
            }
            bit >>= 1;
        } while (bit);

        return quotient;
    }
}

static int WordsGreater(LPDWORD a, LPDWORD b, DWORD nLimbs)
{
    int i;

    for (i = (int)nLimbs - 1; i >= 0; --i) {
        if (a[i] > b[i]) {
            return 1;
        }
        if (a[i] < b[i]) {
            return 0;
        }
    }
    return 0;
}

BOOL Mod(LPDWORD A, LPDWORD B, LPDWORD R, DWORD T, DWORD N)
{
    DWORD aLen;
    DWORD mLen;
    DWORD workBytes;
    BYTE stackBuf[512];
    LPDWORD work;
    LPDWORD prod;
    LPDWORD prodHi;
    LPDWORD rem;
    LPDWORD modW;
    HLOCAL heap;
    int count;
    LPDWORD tail;
    LPDWORD subPos;

    if (N == 0 || T == 0) {
        return FALSE;
    }

    aLen = TrimLen(A, T);
    mLen = TrimLen(B, N);
    if (B[0] == 0) {
        return FALSE;
    }

    if (aLen < mLen) {
        CopyWords(R, A, N * sizeof(DWORD));
        return TRUE;
    }

    workBytes = (aLen + 2 * mLen + 3) * sizeof(DWORD) + 12;
    heap = NULL;
    if (workBytes <= sizeof(stackBuf)) {
        work = (LPDWORD)stackBuf;
    } else {
        heap = LocalAlloc(LMEM_FIXED, workBytes);
        if (!heap) {
            return FALSE;
        }
        work = (LPDWORD)heap;
    }

    ZeroWords(work, workBytes);
    prod = work;
    prodHi = work + mLen;
    rem = work + mLen + 1;
    modW = work + 2 * mLen + 2;

    CopyWords(rem, B, mLen * sizeof(DWORD));
    rem[mLen] = 0;
    CopyWords(modW, A, aLen * sizeof(DWORD));
    modW[aLen] = 0;

    count = (int)aLen - (int)mLen;
    if (count >= 0) {
        tail = modW + count;
        subPos = modW + aLen;

        while (count >= 0) {
            DWORD q;
            DWORD carry;

            if (mLen > 1) {
                q = EstimateQuotient(subPos[0],
                                     subPos[-1],
                                     B[mLen - 1],
                                     B[mLen - 2]);
            } else {
                q = EstimateQuotient(subPos[0], 0, B[0], 0);
            }
            if (q == 0) {
                q = 1;
            }

            carry = BaseMult(prod, q, B, mLen);
            prodHi[0] = carry;

            while (WordsGreater(prod, tail, mLen + 1)) {
                Sub(prod, prod, rem, mLen + 1);
            }

            Sub(tail, tail, prod, mLen + 1);

            if (Compare(tail, rem, mLen + 1) >= 0) {
                continue;
            }

            --count;
            --tail;
            --subPos;
        }
    }

    CopyWords(R, modW, mLen * sizeof(DWORD));
    if (N > mLen) {
        ZeroWords(R + mLen, (N - mLen) * sizeof(DWORD));
    }

    if (heap) {
        LocalFree(heap);
    }
    return TRUE;
}

BOOL Divide(LPDWORD qi, LPDWORD ri, LPDWORD uu, LPDWORD vv, DWORD ll, DWORD kk)
{
    (void)qi;
    (void)ri;
    (void)uu;
    (void)vv;
    (void)ll;
    (void)kk;
    return FALSE;
}

BOOL GCD(LPDWORD u3, LPDWORD u1, LPDWORD u2, LPDWORD u, LPDWORD v, DWORD k)
{
    (void)u3;
    (void)u1;
    (void)u2;
    (void)u;
    (void)v;
    (void)k;
    return FALSE;
}
