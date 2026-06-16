#include <stdio.h>
#include <string.h>
#include <windows.h>
#include <rsa_math.h>
#include <rsa_fast.h>

static void HexWords(const char* label, LPDWORD words, DWORD count)
{
    DWORD i;
    printf("%s:", label);
    for (i = 0; i < count; ++i) {
        printf("%08X", words[i]);
    }
    printf("\n");
}

static void TestEstimateQuotient(void)
{
    DWORD q = 0;

    q = EstimateQuotient(0x00000001, 0x00000000, 0x00000003, 0x00000000);
    printf("estimate_q1:%08X\n", q);

    q = EstimateQuotient(0x80000000, 0x00000001, 0x00000001, 0x80000000);
    printf("estimate_q2:%08X\n", q);
}

static void TestMultiplySmall(void)
{
    static DWORD got[4];
    DWORD b[2] = { 2, 0 };
    DWORD c[2] = { 3, 0 };

    ZeroMemory(got, sizeof(got));
    Multiply(got, b, c, 2);
    HexWords("multiply_small", got, 4);
}

static void TestSquareSmall(void)
{
    static DWORD got[4];
    DWORD b[2] = { 5, 0 };

    ZeroMemory(got, sizeof(got));
    Square(got, b, 2);
    HexWords("square_small", got, 4);
}

static void TestModSmall(void)
{
    DWORD a[2] = { 5, 0 };
    DWORD m[2] = { 3, 0 };
    static DWORD got[2];

    ZeroMemory(got, sizeof(got));
    if (!Mod(a, m, got, 2, 2)) {
        printf("mod_small:FAIL\n");
        return;
    }
    HexWords("mod_small", got, 2);
}

static void TestModDouble(void)
{
    static const DWORD kMod[24] = {
        0xFFFFFFFF, 0xFFFFFFFF, 0xC90FDAA2, 0x2168C234,
        0xC4C6628B, 0x80DC1CD1, 0x29024E08, 0x8A67CC74,
        0x020BBEA6, 0x3B139B22, 0x514A0879, 0x8E3404DD,
        0xEF9519B3, 0xCD3A431B, 0x302B0A6D, 0xF25F1437,
        0x4FE1356D, 0x6D51C245, 0xE485B576, 0x625E7EC6,
        0xF44C42E9, 0xA63A3620, 0xFFFFFFFF, 0xFFFFFFFF
    };
    static DWORD prod[48];
    static DWORD got[48];
    DWORD a[48];
    DWORD b[24];

    ZeroMemory(a, sizeof(a));
    ZeroMemory(b, sizeof(b));
    a[0] = 2;
    b[0] = 2;

    ZeroMemory(prod, sizeof(prod));
    Multiply(prod, a, b, 24);
    HexWords("mod768_mul", prod, 48);

    ZeroMemory(got, sizeof(got));
    if (!Mod(prod, (LPDWORD)kMod, got, 48, 24)) {
        printf("mod768:FAIL\n");
        return;
    }
    HexWords("mod768", got, 24);
}

static void TestModLarge(void)
{
    static const DWORD kMod[24] = {
        0xFFFFFFFF, 0xFFFFFFFF, 0xC90FDAA2, 0x2168C234,
        0xC4C6628B, 0x80DC1CD1, 0x29024E08, 0x8A67CC74,
        0x020BBEA6, 0x3B139B22, 0x514A0879, 0x8E3404DD,
        0xEF9519B3, 0xCD3A431B, 0x302B0A6D, 0xF25F1437,
        0x4FE1356D, 0x6D51C245, 0xE485B576, 0x625E7EC6,
        0xF44C42E9, 0xA63A3620, 0xFFFFFFFF, 0xFFFFFFFF
    };
    static const DWORD kBase[24] = {
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 2
    };
    static DWORD sq[48];
    static DWORD got[48];

    ZeroMemory(sq, sizeof(sq));
    Square(sq, (LPDWORD)kBase, 24);
    HexWords("mod768_sq", sq, 48);

    ZeroMemory(got, sizeof(got));
    if (!Mod(sq, (LPDWORD)kMod, got, 48, 24)) {
        printf("mod768_sqmod:FAIL\n");
        return;
    }
    HexWords("mod768_sqmod", got, 24);
}

int __cdecl main(void)
{
    TestMultiplySmall();
    TestSquareSmall();
    TestModSmall();
    TestModDouble();
    TestModLarge();
    TestEstimateQuotient();
    printf("math_test:OK\n");
    return 0;
}
