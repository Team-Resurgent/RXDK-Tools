#include <windows.h>
#include <rsa_fast.h>

DWORD __stdcall BaseMult(LPDWORD A, DWORD B, LPDWORD C, DWORD N)
{
    DWORD carry = 0;
    DWORD i;

    if (B == 1)
    {
        for (i = 0; i < N; ++i)
        {
            A[i] = C[i];
        }
        return 0;
    }

    for (i = 0; i < N; ++i)
    {
        unsigned __int64 prod = (unsigned __int64)B * (unsigned __int64)C[i] + carry;
        A[i] = (DWORD)prod;
        carry = (DWORD)(prod >> 32);
    }
    return carry;
}

static DWORD MulAddWords(LPDWORD A, DWORD B, LPDWORD C, DWORD N, BOOL subtract)
{
    DWORD carry = 0;
    DWORD i;

    if (B == 0)
    {
        return 0;
    }
    if (B == 1)
    {
        return subtract ? Sub(A, A, C, N) : Add(A, A, C, N);
    }

    for (i = 0; i < N; ++i)
    {
        unsigned __int64 prod = (unsigned __int64)B * (unsigned __int64)C[i];
        DWORD tLo = (DWORD)prod + carry;
        DWORD tHi = (DWORD)(prod >> 32) + (tLo < (DWORD)prod ? 1u : 0u);
        DWORD a = A[i];

        if (subtract)
        {
            A[i] = a - tLo;
            if (a < tLo)
            {
                ++tHi;
            }
            carry = tHi;
        }
        else
        {
            unsigned __int64 sum = (unsigned __int64)a + tLo;
            A[i] = (DWORD)sum;
            tHi += (DWORD)(sum >> 32);
            carry = tHi;
        }
    }
    return carry;
}

DWORD __stdcall Accumulate(LPDWORD A, DWORD B, LPDWORD C, DWORD N)
{
    return MulAddWords(A, B, C, N, FALSE);
}

DWORD __stdcall Reduce(LPDWORD A, DWORD B, LPDWORD C, DWORD N)
{
    return MulAddWords(A, B, C, N, TRUE);
}

void __stdcall AccumulateSquares(LPDWORD A, LPDWORD B, DWORD blen)
{
    DWORD pending = 0;
    DWORD i;

    for (i = 0; i < blen; ++i)
    {
        unsigned __int64 sq = (unsigned __int64)B[i] * (unsigned __int64)B[i];
        unsigned __int64 t = pending + sq;
        DWORD aLo = A[i * 2];
        DWORD aHi = A[i * 2 + 1];
        unsigned __int64 sumLo = (unsigned __int64)aLo + (DWORD)t;
        unsigned __int64 sumHi = (t >> 32) + (sumLo >> 32) + aHi;

        pending = (DWORD)(sumHi >> 32);
        A[i * 2] = (DWORD)sumLo;
        A[i * 2 + 1] = (DWORD)sumHi;
    }
}
