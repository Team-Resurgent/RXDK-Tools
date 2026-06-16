#pragma once

// Minimal Win32 list helpers for imagebld (avoid Xbox ntrtl.h / vendor windows.h stub).
#ifndef _IMAGEBLD_WIN32_H_
#define _IMAGEBLD_WIN32_H_

typedef struct _IMAGEBLD_LIST_ENTRY {
    struct _IMAGEBLD_LIST_ENTRY *Flink;
    struct _IMAGEBLD_LIST_ENTRY *Blink;
} IMAGEBLD_LIST_ENTRY, *PIMAGEBLD_LIST_ENTRY;

#ifndef LIST_ENTRY
#define LIST_ENTRY IMAGEBLD_LIST_ENTRY
#define PLIST_ENTRY PIMAGEBLD_LIST_ENTRY
#endif

#ifndef InitializeListHead
#define InitializeListHead(ListHead) \
    ((ListHead)->Flink = (ListHead)->Blink = (ListHead))
#endif

#ifndef InsertHeadList
#define InsertHeadList(ListHead, Entry) \
    { \
        PLIST_ENTRY _EXF_Flink; \
        PLIST_ENTRY _EXF_ListHead; \
        _EXF_ListHead = (ListHead); \
        _EXF_Flink = _EXF_ListHead->Flink; \
        (Entry)->Flink = _EXF_Flink; \
        (Entry)->Blink = _EXF_ListHead; \
        _EXF_Flink->Blink = (Entry); \
        _EXF_ListHead->Flink = (Entry); \
    }
#endif

#ifndef InsertTailList
#define InsertTailList(ListHead, Entry) \
    { \
        PLIST_ENTRY _EXF_Link = (ListHead); \
        PLIST_ENTRY _EXF_Blink = _EXF_Link->Blink; \
        (Entry)->Flink = _EXF_Link; \
        (Entry)->Blink = _EXF_Blink; \
        _EXF_Blink->Flink = (Entry); \
        _EXF_Link->Blink = (Entry); \
    }
#endif

#ifndef IsListEmpty
#define IsListEmpty(ListHead) ((ListHead)->Flink == (ListHead))
#endif

// Xbox PE types (not in desktop winnt.h).
#ifndef IMAGE_SUBSYSTEM_XBOX
#define IMAGE_SUBSYSTEM_XBOX 14
#endif
#ifndef IMAGE_SUBSYSTEM_WINDOWS_CUI
#define IMAGE_SUBSYSTEM_WINDOWS_CUI 3
#endif
#ifndef IMAGE_REL_BASED_SECTION
#define IMAGE_REL_BASED_SECTION 6
#endif
#ifndef IMAGE_REL_BASED_REL32
#define IMAGE_REL_BASED_REL32 7
#endif

#endif /* _IMAGEBLD_WIN32_H_ */
