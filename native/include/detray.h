/*
 * The macOS tray's ABI: DiffEngineTray's status bar item, which is PlatformHeadsMac's StatusItem.
 * A library of its own beside the viewer's (diffengine_viewer), built from the same Swift package,
 * so a change to either never moves the other's version.
 *
 * Followed by native/swift/Sources/Detray and src/DiffEngineTray.Core/Detray.cs. Bump
 * DETRAY_VERSION whenever a struct changes or a field changes meaning: the managed side refuses a
 * library whose version is not the one it was built for.
 *
 * Every call that touches AppKit is the managed side's main thread's to make, as AppKit requires.
 */
#ifndef DETRAY_H
#define DETRAY_H

#include <stdint.h>

#define DETRAY_API __attribute__((visibility("default")))

#ifdef __cplusplus
extern "C" {
#endif

/*
 * 1: the status item and its menu.
 * 2: detray_on_session_end, and a logout, restart or shutdown no longer reported by
 *    detray_quitting. A library from before cancels the logout that asked it to quit.
 */
#define DETRAY_VERSION 2

/* What detray_on_session_end calls. */
typedef void (*DetraySessionEnd)(void);

/* PlatformHeads' TrayMenuFlags and PlatformHeadsMac's StatusMenuFlags, which write and read them. */
#define DETRAY_ENABLED 1
#define DETRAY_SEPARATOR 2
/* A toggle, drawn with a check when DETRAY_CHECKED is set as well. */
#define DETRAY_CHECKABLE 4
#define DETRAY_CHECKED 8

/*
 * One menu item. The menu is one array, each submenu's items after the item that opens it, and
 * parent is the index of that item in the same array, or -1 for an item of the menu itself. The
 * strings are UTF-8, and read only during the call they are passed to. icon names a glyph handed
 * over by detray_set_menu_icon, or is empty.
 */
typedef struct DetrayItem {
    const char* id;
    const char* label;
    const char* icon;
    int32_t flags;
    int32_t parent;
} DetrayItem;

#ifndef DETRAY_TYPES_ONLY

DETRAY_API int32_t detray_version(void);

/* The application, as an accessory with no Dock icon, and the item in the status bar. 1 on success. */
DETRAY_API int32_t detray_init(void);

/* An icon's picture, by the managed TrayIcon's value. A PNG, drawn at the menu bar's height. */
DETRAY_API void detray_set_icon(int32_t icon, const uint8_t* png, int32_t length);

DETRAY_API void detray_set_menu_icon(const char* name, const uint8_t* png, int32_t length);

/* What the item shows: which icon, its tooltip, and the menu built when it is next opened. */
DETRAY_API void detray_apply(int32_t icon, const char* tooltip, const DetrayItem* items, int32_t count);

/*
 * Dispatches AppKit's events for up to a tenth of a second, and returns at once when one has left
 * an item chosen or a quit asked for. The menu's own tracking loop runs inside it.
 */
DETRAY_API void detray_pump(void);

/*
 * The id of an item chosen since the last poll, written to buffer, its whole length in bytes
 * returned, or 0 for none. Chosen items are handed over one a poll, in the order they were chosen.
 */
DETRAY_API int32_t detray_poll(char* buffer, int32_t capacity);

/*
 * 1 once AppKit has been asked to quit other than by the session ending: with no Dock icon, by
 * something that sent the app a quit event, such as AppleScript's quit. Refused, for the managed
 * side to end its loop the way Exit does.
 */
DETRAY_API int32_t detray_quitting(void);

/*
 * Has ended called at a logout, restart or shutdown, from inside the detray_pump that dispatched
 * AppKit's quit, on the main thread. AppKit ends the process as it returns, so it is the one
 * chance to stage the queue: refused, the quit would cancel the logout. Read by detray_init, so
 * set before it; with none, a session's end is refused and reported by detray_quitting.
 */
DETRAY_API void detray_on_session_end(DetraySessionEnd ended);

/*
 * The menu as the last apply left it, a line an item, as text, written to buffer with its whole
 * length returned. For tests, which can make no status item: neither this nor detray_apply needs
 * detray_init.
 */
DETRAY_API int32_t detray_describe(char* buffer, int32_t capacity);

DETRAY_API void detray_shutdown(void);

#endif

#ifdef __cplusplus
}
#endif

#endif
