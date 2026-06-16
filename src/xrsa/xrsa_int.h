#pragma once

#include <windows.h>
#include <benaloh.h>
#include <rsa.h>
#include <sha.h>

#ifndef RSA32API
#define RSA32API __stdcall
#endif

#define XRSA_MAX_WORDS 72

void RSA32API DWORDToBigEndian(unsigned char* dst, DWORD* src, unsigned int count);

int XrsaCompare(LPDWORD a, LPDWORD b, DWORD n);
DWORD XrsaDigitLen(LPDWORD a, DWORD n);
DWORD XrsaBitLen(LPDWORD a, DWORD n);
void XrsaSetValDWORD(LPDWORD num, DWORD val, DWORD len);
void XrsaCopyWords(LPDWORD dst, const LPDWORD src, DWORD len);
void XrsaZeroWords(LPDWORD a, DWORD len);
DWORD XrsaAdd(LPDWORD a, LPDWORD b, LPDWORD c, DWORD n);
DWORD XrsaSub(LPDWORD a, LPDWORD b, LPDWORD c, DWORD n);
void XrsaMultiply(LPDWORD a, LPDWORD b, LPDWORD c, DWORD n);
void XrsaSquare(LPDWORD a, LPDWORD b, DWORD n);
BOOL XrsaMod(LPDWORD a, LPDWORD modulus, LPDWORD result, DWORD aWords, DWORD modWords);
BOOL XrsaModMultiply(LPDWORD a, LPDWORD b, LPDWORD c, LPDWORD mod, DWORD n);
BOOL XrsaModSquare(LPDWORD a, LPDWORD b, LPDWORD mod, DWORD n);
BOOL XrsaModExp(LPDWORD a, LPDWORD b, LPDWORD c, LPDWORD mod, DWORD n);
BOOL XrsaModRoot(LPDWORD m, LPDWORD c, LPDWORD pp, LPDWORD qq,
    LPDWORD dp, LPDWORD dq, LPDWORD cr, DWORD pSize);
