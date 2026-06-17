#include <windows.h>
#include <rsa_fast.h>
#include <benaloh.h>

DWORD __stdcall Add(LPDWORD A, LPDWORD B, LPDWORD C, DWORD N)
{
    DWORD carry = 0;
    DWORD i;

    for (i = 0; i < N; ++i)
    {
        unsigned __int64 sum =
            (unsigned __int64)B[i] + (unsigned __int64)C[i] + carry;
        A[i] = (DWORD)sum;
        carry = (DWORD)(sum >> 32);
    }
    return carry;
}

DWORD __stdcall Sub(LPDWORD A, LPDWORD B, LPDWORD C, DWORD N)
{
    DWORD borrow = 0;
    DWORD i;

    for (i = 0; i < N; ++i)
    {
        unsigned __int64 bv = (unsigned __int64)C[i] + borrow;
        if ((unsigned __int64)B[i] < bv)
        {
            A[i] = (DWORD)((unsigned __int64)B[i] + (1ULL << 32) - bv);
            borrow = 1;
        }
        else
        {
            A[i] = (DWORD)((unsigned __int64)B[i] - bv);
            borrow = 0;
        }
    }
    return borrow;
}

DWORD __stdcall BenalohEstimateQuotient(DWORD a1, DWORD a2, DWORD m1)
{
    if (m1 == 0)
    {
        return 0;
    }
    if (a1 >= m1)
    {
        return 0xffffffffu;
    }
    {
        unsigned __int64 num = ((unsigned __int64)a1 << 32) | (unsigned __int64)a2;
        return (DWORD)(num / m1);
    }
}
