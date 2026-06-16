#include <windows.h>

void __stdcall DWORDToBigEndian(unsigned char* dst, DWORD* src, unsigned int count)
{
    unsigned int i;
    for (i = 0; i < count; ++i) {
        dst[i * 4] = (unsigned char)(src[i] >> 24);
        dst[i * 4 + 1] = (unsigned char)(src[i] >> 16);
        dst[i * 4 + 2] = (unsigned char)(src[i] >> 8);
        dst[i * 4 + 3] = (unsigned char)(src[i]);
    }
}

void __stdcall DWORDFromBigEndian(DWORD* dst, unsigned char* src, unsigned int count)
{
    unsigned int i;
    for (i = 0; i < count; ++i) {
        dst[i] = ((DWORD)src[i * 4] << 24) |
                 ((DWORD)src[i * 4 + 1] << 16) |
                 ((DWORD)src[i * 4 + 2] << 8) |
                 (DWORD)src[i * 4 + 3];
    }
}

void __stdcall DWORDToLittleEndian(unsigned char* dst, DWORD* src, unsigned int count)
{
    unsigned int i;
    for (i = 0; i < count; ++i) {
        dst[i * 4] = (unsigned char)(src[i]);
        dst[i * 4 + 1] = (unsigned char)(src[i] >> 8);
        dst[i * 4 + 2] = (unsigned char)(src[i] >> 16);
        dst[i * 4 + 3] = (unsigned char)(src[i] >> 24);
    }
}

void __stdcall DWORDFromLittleEndian(DWORD* dst, unsigned char* src, unsigned int count)
{
    unsigned int i;
    for (i = 0; i < count; ++i) {
        dst[i] = (DWORD)src[i * 4] |
                 ((DWORD)src[i * 4 + 1] << 8) |
                 ((DWORD)src[i * 4 + 2] << 16) |
                 ((DWORD)src[i * 4 + 3] << 24);
    }
}
