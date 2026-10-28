import AppKit
import CDetray
import Foundation
import PlatformHeadsMac

/// The entry points of native/include/detray.h, over PlatformHeadsMac's StatusItem.
///
/// The header is imported for its struct and constants only, with DETRAY_TYPES_ONLY, so these are
/// the definitions of those symbols rather than a second declaration of them. Each one that
/// touches AppKit runs inside its own autorelease pool, for the reason the viewer's Exports.swift
/// gives: nothing here calls `NSApplication.run`, which would drain one an event.

@_cdecl("detray_version")
public func detrayVersion() -> Int32 {
    Int32(DETRAY_VERSION)
}

@_cdecl("detray_init")
public func detrayInit() -> Int32 {
    autoreleasepool {
        Tray.shared.start() ? 1 : 0
    }
}

@_cdecl("detray_set_icon")
public func detraySetIcon(_ icon: Int32, _ png: UnsafePointer<UInt8>?, _ length: Int32) {
    guard let png, length > 0 else {
        return
    }

    autoreleasepool {
        Tray.shared.item.setIcon(icon, png: Data(bytes: png, count: Int(length)))
    }
}

@_cdecl("detray_set_menu_icon")
public func detraySetMenuIcon(_ name: UnsafePointer<CChar>?, _ png: UnsafePointer<UInt8>?, _ length: Int32) {
    guard let name, let png, length > 0 else {
        return
    }

    autoreleasepool {
        Tray.shared.item.setMenuIcon(String(cString: name), png: Data(bytes: png, count: Int(length)))
    }
}

@_cdecl("detray_apply")
public func detrayApply(_ icon: Int32, _ tooltip: UnsafePointer<CChar>?, _ items: UnsafePointer<DetrayItem>?, _ count: Int32) {
    autoreleasepool {
        Tray.shared.item.apply(
            icon: icon,
            tooltip: tooltip.map { String(cString: $0) } ?? "",
            items: MenuItems.read(items, count))
    }
}

@_cdecl("detray_pump")
public func detrayPump() {
    autoreleasepool {
        Tray.shared.pump()
    }
}

@_cdecl("detray_poll")
public func detrayPoll(_ buffer: UnsafeMutablePointer<CChar>?, _ capacity: Int32) -> Int32 {
    guard let chosen = Tray.shared.takeChosen() else {
        return 0
    }

    return Utf8Buffer.write(chosen, buffer, capacity)
}

@_cdecl("detray_quitting")
public func detrayQuitting() -> Int32 {
    Tray.shared.quitting ? 1 : 0
}

@_cdecl("detray_describe")
public func detrayDescribe(_ buffer: UnsafeMutablePointer<CChar>?, _ capacity: Int32) -> Int32 {
    Utf8Buffer.write(MenuItems.describe(Tray.shared.item.items), buffer, capacity)
}

@_cdecl("detray_shutdown")
public func detrayShutdown() {
    autoreleasepool {
        Tray.shared.stop()
    }
}
