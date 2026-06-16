#pragma once

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <windows.h>

#include <benaloh.h>
#include <rsa.h>
#include <sha.h>
#include <xcrypt.h>

void HexDumpLine(const char* label, const unsigned char* data, unsigned int cb);
int TestSha(void);
#ifndef XRSA_PARTIAL
int TestBenalohModExp(void);
int TestBsafeRoundtrip(void);
int TestXcSign(void);
DWORD XCCalcSigSize(PBYTE pbPrivateKey);
BOOLEAN XCSignDigest(PBYTE pbDigest, PBYTE pbPrivateKey, PBYTE pbSig);
#endif

extern const unsigned char g_ImgbPrivateKeyData[];
extern const unsigned char g_ImgbPublicKeyData[];
