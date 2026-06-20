#pragma once

#include <string>
#include <vector>

namespace NativeFolderOps
{
    constexpr const char kAddConsoleSegment[] = "?Add Xbox";

    constexpr ULONG kSfgaoCanlink = 0x00000004;
    constexpr ULONG kSfgaoCanrename = 0x00000010;
    constexpr ULONG kSfgaoFolder = 0x20000000;
    constexpr ULONG kSfgaoHassubfolder = 0x80000000;
    constexpr ULONG kSfgaoBrowsable = 0x08000000;
    constexpr ULONG kSfgaoHaspropsheet = 0x00000040;
    constexpr ULONG kSfgaoCandelete = 0x00000020;
    constexpr ULONG kSfgaoCancopy = 0x00000001;
    constexpr ULONG kSfgaoCanmove = 0x00000002;
    constexpr ULONG kSfgaoDropTarget = 0x00000100;

    constexpr ULONG kRootAttributes =
        kSfgaoCanlink | kSfgaoCanrename | kSfgaoFolder | kSfgaoHassubfolder | kSfgaoBrowsable;

    constexpr ULONG kConsoleAttributes =
        kSfgaoCanlink | kSfgaoCandelete | kSfgaoHaspropsheet | kSfgaoFolder | kSfgaoHassubfolder | kSfgaoBrowsable;

    constexpr ULONG kAddConsoleAttributes = kSfgaoCanlink;

    constexpr ULONG kVolumeAttributes =
        kSfgaoCanlink | kSfgaoHaspropsheet | kSfgaoDropTarget | kSfgaoFolder | kSfgaoHassubfolder | kSfgaoBrowsable;

    constexpr ULONG kDirectoryAttributes =
        kSfgaoCancopy | kSfgaoCanmove | kSfgaoCanlink | kSfgaoCanrename | kSfgaoCandelete |
        kSfgaoHaspropsheet | kSfgaoFolder | kSfgaoHassubfolder | kSfgaoBrowsable;

    constexpr ULONG kFileAttributes =
        kSfgaoCancopy | kSfgaoCanmove | kSfgaoCanrename | kSfgaoCandelete | kSfgaoHaspropsheet;

    LPITEMIDLIST CreateSimplePidl(LPCSTR segment);
    LPITEMIDLIST CombinePidls(LPCITEMIDLIST parent, LPCITEMIDLIST relativeChild);
    HRESULT ParseRootDisplayName(
        LPCWSTR displayName,
        ULONG* pchEaten,
        LPITEMIDLIST* ppidl,
        ULONG* pdwAttributes);
    HRESULT ParseRelativeDisplayName(
        LPCITEMIDLIST folderPidl,
        LPCWSTR displayName,
        ULONG* pchEaten,
        LPITEMIDLIST* ppidl,
        ULONG* pdwAttributes);
    std::string GetLastSegment(LPCITEMIDLIST pidl);
    UINT CountPidlSegments(LPCITEMIDLIST pidl);
    bool ConsoleNameExists(LPCSTR segment);
    bool IsPlausibleChildBind(LPCITEMIDLIST folderPidl, LPCITEMIDLIST childPidl);
    bool ShouldBindWithoutManagedInner(LPCITEMIDLIST folderPidl, LPCITEMIDLIST childPidl);
    HRESULT CompareSimplePidls(LPCITEMIDLIST pidl1, LPCITEMIDLIST pidl2);
    std::string GetNamespaceRelativePath(LPCITEMIDLIST pidl);
    ULONG GetChildAttributes(LPCSTR segment);
    HRESULT GetDisplayName(LPCITEMIDLIST pidl, DWORD uFlags, STRRET* pName);
    HRESULT GetDetailsOf(LPCITEMIDLIST pidl, UINT iColumn, SHELLDETAILS* psd);
    HRESULT EnumRootObjects(DWORD grfFlags, std::vector<LPITEMIDLIST>& pidls);
    HRESULT GetRootChildCount(DWORD grfFlags, UINT* count);
    HRESULT CreateNativeEnumIdList(std::vector<LPITEMIDLIST>& pidls, IEnumIDList** ppenumIDList);
    LPITEMIDLIST CreateNamespaceRelativePidl(LPCITEMIDLIST absolutePidl);
}
