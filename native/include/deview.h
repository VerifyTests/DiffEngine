/*
 * DiffEngineViewer native renderer.
 *
 * This is a renderer for a screen model, not an ImGui binding. All application logic, state and
 * layout live in C#; the managed side marshals one flat, blittable description of the frame and
 * this library turns it into ImGui calls. That keeps the export surface at a dozen functions
 * instead of cimgui's ~1000, makes interop one call per frame instead of thousands, and means the
 * structure the snapshot tests verify is the exact structure drawn here.
 *
 * Every string is a byte offset and length into DeviewScreen.strings, a single UTF-8 blob, so a
 * frame is one allocation on the managed side and no per-string marshalling.
 */
#ifndef DEVIEW_H
#define DEVIEW_H

#include <stdint.h>

#if defined(_WIN32)
#define DEVIEW_API __declspec(dllexport)
#else
#define DEVIEW_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Keep in sync with RowKind.cs */
enum DeviewRowKind {
    DEVIEW_ROW_UNCHANGED = 0,
    DEVIEW_ROW_ADDED = 1,
    DEVIEW_ROW_REMOVED = 2,
    DEVIEW_ROW_MODIFIED = 3,
    DEVIEW_ROW_FILLER = 4
};

enum DeviewButtonFlags {
    DEVIEW_BUTTON_ENABLED = 1 << 0
};

enum DeviewQueueFlags {
    DEVIEW_QUEUE_SELECTED = 1 << 0,
    DEVIEW_QUEUE_FAILED = 1 << 1
};

typedef struct DeviewRow {
    int32_t kind;
    /* -1 when the row is filler and has no line number. */
    int32_t lineNumber;
    int32_t textOffset;
    int32_t textLength;
} DeviewRow;

typedef struct DeviewPane {
    int32_t headerOffset;
    int32_t headerLength;
    int32_t rowOffset;
    int32_t rowCount;
    int32_t scrollTop;
    int32_t totalRows;
} DeviewPane;

typedef struct DeviewButton {
    int32_t labelOffset;
    int32_t labelLength;
    int32_t flags;
} DeviewButton;

typedef struct DeviewQueueItem {
    int32_t labelOffset;
    int32_t labelLength;
    int32_t flags;
} DeviewQueueItem;

typedef struct DeviewScreen {
    const uint8_t* strings;
    int32_t stringsLength;

    const DeviewPane* panes;
    int32_t paneCount;

    const DeviewRow* rows;
    int32_t rowCount;

    const DeviewButton* buttons;
    int32_t buttonCount;

    const DeviewQueueItem* queue;
    int32_t queueCount;

    int32_t titleOffset;
    int32_t titleLength;
    int32_t subtitleOffset;
    int32_t subtitleLength;
    int32_t statusOffset;
    int32_t statusLength;
} DeviewScreen;

/* Keep in sync with CommandKind.cs */
enum DeviewKey {
    DEVIEW_KEY_NONE = 0,
    DEVIEW_KEY_SCROLL_UP = 1,
    DEVIEW_KEY_SCROLL_DOWN = 2,
    DEVIEW_KEY_PAGE_UP = 3,
    DEVIEW_KEY_PAGE_DOWN = 4,
    DEVIEW_KEY_HOME = 5,
    DEVIEW_KEY_END = 6,
    DEVIEW_KEY_NEXT_CHANGE = 7,
    DEVIEW_KEY_PREVIOUS_CHANGE = 8,
    DEVIEW_KEY_NEXT_ITEM = 9,
    DEVIEW_KEY_PREVIOUS_ITEM = 10,
    DEVIEW_KEY_ACCEPT = 11,
    DEVIEW_KEY_DISCARD = 12,
    DEVIEW_KEY_ACCEPT_ALL = 13,
    DEVIEW_KEY_QUIT = 14
};

typedef struct DeviewInput {
    int32_t key;
    /* Index into DeviewScreen.buttons, or -1. */
    int32_t clickedButton;
    /* Index into DeviewScreen.queue, or -1. */
    int32_t clickedQueueItem;
    int32_t scrollDelta;
    /* Set when the user asked to close the window; the managed side decides hide versus exit. */
    int32_t closeRequested;
    /*
     * The window size in character cells, not pixels. Measured here from the font that was
     * actually loaded, because this side is the only one that knows it. Reporting pixels and
     * having the managed side divide by a constant is what left the viewer with no DPI handling.
     */
    int32_t columns;
    int32_t rows;
} DeviewInput;

/*
 * Bumped whenever the structs above change, or what a field means changes, so a stale native
 * library is detected not crashed.
 *
 * 2: DeviewInput.columns and rows carry character cells rather than pixels.
 */
#define DEVIEW_VERSION 2

/*
 * The Swift implementation imports this header for the struct layouts, because Swift does not
 * guarantee its own, and then defines the entry points itself with @_cdecl. It defines
 * DEVIEW_TYPES_ONLY so it does not also import prototypes for symbols it is about to provide.
 */
#ifndef DEVIEW_TYPES_ONLY

/*
 * Returns 1 on success. fontTtf may be NULL, in which case a built in font is used.
 * hidden starts the window offscreen, which the pixel snapshot tests rely on.
 */
DEVIEW_API int32_t deview_init(
    int32_t width,
    int32_t height,
    const char* title,
    const uint8_t* fontTtf,
    int32_t fontLength,
    float fontSize,
    int32_t hidden);

/* Draws one frame. Returns 0 once the window has been closed. */
DEVIEW_API int32_t deview_present(const DeviewScreen* screen);

DEVIEW_API void deview_poll_input(DeviewInput* input);

/* Renders one frame offscreen and writes it to pngPath. Returns 1 on success. */
DEVIEW_API int32_t deview_capture(
    const DeviewScreen* screen,
    int32_t width,
    int32_t height,
    const char* pngPath);

DEVIEW_API void deview_set_hidden(int32_t hidden);

DEVIEW_API void deview_focus(void);

DEVIEW_API void deview_shutdown(void);

DEVIEW_API int32_t deview_version(void);

#endif /* DEVIEW_TYPES_ONLY */

#ifdef __cplusplus
}
#endif

#endif
