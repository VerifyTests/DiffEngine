/*
 * Brings the tray's ABI structs and constants into Swift without their prototypes: the library
 * provides those symbols itself, with @_cdecl, as the viewer's does. Reached by a relative include
 * rather than copied, for the reason deview_bridge.h gives.
 */
#ifndef DETRAY_BRIDGE_H
#define DETRAY_BRIDGE_H

#define DETRAY_TYPES_ONLY
#include "../../../../include/detray.h"

#endif
