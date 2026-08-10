/*
 * Brings the ABI structs into Swift without their prototypes: this library provides those symbols
 * itself, with @_cdecl, so importing declarations for them as well would only invite a clash.
 *
 * The canonical header is reached by a relative include rather than copied, because two copies of
 * a struct layout is exactly the bug DEVIEW_VERSION exists to catch.
 */
#ifndef DEVIEW_BRIDGE_H
#define DEVIEW_BRIDGE_H

#define DEVIEW_TYPES_ONLY
#include "../../../../include/deview.h"

#endif
