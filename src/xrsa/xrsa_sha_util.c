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
