#include "neighborhood.h"
#include "propdraw.h"

static int IntSqrt(unsigned long dwNum)
{
    DWORD dwSqrt = 0, dwRemain = 0, dwTry;
    int i;

    for (i = 0; i < 16; ++i)
    {
        dwRemain = (dwRemain << 2) | (dwNum >> 30);
        dwSqrt <<= 1;
        dwTry = dwSqrt * 2 + 1;

        if (dwRemain >= dwTry)
        {
            dwRemain -= dwTry;
            dwSqrt |= 0x01;
        }

        dwNum <<= 2;
    }

    return (int)dwSqrt;
}

void DrawPie(HDC hDC, LPCRECT lprcItem, UINT uPctX10, BOOL TrueZr100, UINT uOffset, const COLORREF *lpColors)
{
    int cx, cy, rx, ry, x, y;
    int uQPctX10;
    RECT rcItem;
    HRGN hEllRect, hEllipticRgn, hRectRgn;
    HBRUSH hBrush, hOldBrush;
    HPEN hPen, hOldPen;
    DWORD dwOldLayout;

    rcItem = *lprcItem;
    rcItem.left = lprcItem->left;
    rcItem.top = lprcItem->top;
    rcItem.right = lprcItem->right - rcItem.left;
    rcItem.bottom = lprcItem->bottom - rcItem.top - uOffset;

    rx = rcItem.right / 2;
    cx = rcItem.left + rx - 1;
    ry = rcItem.bottom / 2;
    cy = rcItem.top + ry - 1;
    if (rx <= 10 || ry <= 10)
        return;

    dwOldLayout = SetLayout(hDC, 0);

    rcItem.right = rcItem.left + 2 * rx;
    rcItem.bottom = rcItem.top + 2 * ry;

    if (uPctX10 > 1000)
        uPctX10 = 1000;

    uQPctX10 = (uPctX10 % 500) - 250;
    if (uQPctX10 < 0)
        uQPctX10 = -uQPctX10;

    if (uQPctX10 < 120)
    {
        x = IntSqrt(((DWORD)rx * (DWORD)rx * (DWORD)uQPctX10 * (DWORD)uQPctX10) / ((DWORD)uQPctX10 * (DWORD)uQPctX10 + (250L - (DWORD)uQPctX10) * (250L - (DWORD)uQPctX10)));
        y = IntSqrt(((DWORD)rx * (DWORD)rx - (DWORD)x * (DWORD)x) * (DWORD)ry * (DWORD)ry / ((DWORD)rx * (DWORD)rx));
    }
    else
    {
        y = IntSqrt((DWORD)ry * (DWORD)ry * (250L - (DWORD)uQPctX10) * (250L - (DWORD)uQPctX10) / ((DWORD)uQPctX10 * (DWORD)uQPctX10 + (250L - (DWORD)uQPctX10) * (250L - (DWORD)uQPctX10)));
        x = IntSqrt(((DWORD)ry * (DWORD)ry - (DWORD)y * (DWORD)y) * (DWORD)rx * (DWORD)rx / ((DWORD)ry * (DWORD)ry));
    }

    switch (uPctX10 / 250)
    {
    case 1:
        y = -y;
        break;
    case 2:
        break;
    case 3:
        x = -x;
        break;
    default:
        x = -x;
        y = -y;
        break;
    }

    x += cx;
    y += cy;
    x = x < 0 ? 0 : x;

    hEllipticRgn = CreateEllipticRgnIndirect(&rcItem);
    OffsetRgn(hEllipticRgn, 0, (int)uOffset);
    hEllRect = CreateRectRgn(rcItem.left, cy, rcItem.right, cy + (int)uOffset);
    hRectRgn = CreateRectRgn(0, 0, 0, 0);
    CombineRgn(hRectRgn, hEllipticRgn, hEllRect, RGN_OR);
    OffsetRgn(hEllipticRgn, 0, -(int)uOffset);
    CombineRgn(hEllRect, hRectRgn, hEllipticRgn, RGN_DIFF);

    hBrush = CreateSolidBrush(lpColors[DP_FREESHADOW]);
    if (hBrush)
    {
        FillRgn(hDC, hEllRect, hBrush);
        DeleteObject(hBrush);
    }

    if (uPctX10 > 500 && (hBrush = CreateSolidBrush(lpColors[DP_USEDSHADOW])) != NULL)
    {
        DeleteObject(hRectRgn);
        hRectRgn = CreateRectRgn(x, cy, rcItem.right, lprcItem->bottom);
        CombineRgn(hEllipticRgn, hEllRect, hRectRgn, RGN_AND);
        FillRgn(hDC, hEllipticRgn, hBrush);
        DeleteObject(hBrush);
    }

    DeleteObject(hRectRgn);
    DeleteObject(hEllipticRgn);
    DeleteObject(hEllRect);

    hPen = CreatePen(PS_SOLID, 1, GetSysColor(COLOR_WINDOWFRAME));
    hOldPen = (HPEN)SelectObject(hDC, hPen);

    if ((uPctX10 < 100) && (cy == y))
    {
        hBrush = CreateSolidBrush(lpColors[DP_FREECOLOR]);
        hOldBrush = (HBRUSH)SelectObject(hDC, hBrush);
        if ((TrueZr100 == FALSE) || (uPctX10 != 0))
            Pie(hDC, rcItem.left, rcItem.top, rcItem.right, rcItem.bottom, rcItem.left, cy, x, y);
        else
            Ellipse(hDC, rcItem.left, rcItem.top, rcItem.right, rcItem.bottom);
    }
    else if ((uPctX10 > (1000 - 100)) && (cy == y))
    {
        hBrush = CreateSolidBrush(lpColors[DP_USEDCOLOR]);
        hOldBrush = (HBRUSH)SelectObject(hDC, hBrush);
        if ((TrueZr100 == FALSE) || (uPctX10 != 1000))
            Pie(hDC, rcItem.left, rcItem.top, rcItem.right, rcItem.bottom, rcItem.left, cy, x, y);
        else
            Ellipse(hDC, rcItem.left, rcItem.top, rcItem.right, rcItem.bottom);
    }
    else
    {
        hBrush = CreateSolidBrush(lpColors[DP_USEDCOLOR]);
        hOldBrush = (HBRUSH)SelectObject(hDC, hBrush);
        Ellipse(hDC, rcItem.left, rcItem.top, rcItem.right, rcItem.bottom);
        SelectObject(hDC, hOldBrush);
        DeleteObject(hBrush);

        hBrush = CreateSolidBrush(lpColors[DP_FREECOLOR]);
        hOldBrush = (HBRUSH)SelectObject(hDC, hBrush);
        Pie(hDC, rcItem.left, rcItem.top, rcItem.right, rcItem.bottom, rcItem.left, cy, x, y);
    }
    SelectObject(hDC, hOldBrush);
    DeleteObject(hBrush);

    if ((TrueZr100 == FALSE) || ((uPctX10 != 0) && (uPctX10 != 1000)))
    {
        Arc(hDC, rcItem.left, rcItem.top + uOffset, rcItem.right, rcItem.bottom + uOffset, rcItem.left, cy + uOffset, rcItem.right, cy + uOffset - 1);
        MoveToEx(hDC, rcItem.left, cy, NULL);
        LineTo(hDC, rcItem.left, cy + uOffset);
        MoveToEx(hDC, rcItem.right - 1, cy, NULL);
        LineTo(hDC, rcItem.right - 1, cy + uOffset);

        if (uPctX10 > 500)
        {
            MoveToEx(hDC, x, y, NULL);
            LineTo(hDC, x, y + uOffset);
        }
    }
    SelectObject(hDC, hOldPen);
    DeleteObject(hPen);
    SetLayout(hDC, dwOldLayout);
}
