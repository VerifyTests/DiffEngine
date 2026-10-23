import AppKit
import CDeview
import Foundation
import PlatformHeadsMac

/// The eleven entry points of native/include/deview.h, implemented over AppKit and Core Text.
///
/// The header is imported for its struct layouts only, with DEVIEW_TYPES_ONLY, so these are the
/// definitions of those symbols rather than a second declaration of them.
///
/// Every one that touches AppKit runs inside its own autorelease pool. `NSApplication.run` drains
/// a pool per event, but this app never calls it: the managed loop calls in instead, and with no
/// pool pushed objc4 parks everything autoreleased in one it creates for the thread, which drains
/// only when the thread exits. That is the main thread, so everything a frame autoreleased - the
/// events, the attributed strings, the drawing - stayed until the process did. GLFW, which also
/// pumps by hand, does the same.

@_cdecl("deview_version")
public func deviewVersion() -> Int32 {
    Int32(DEVIEW_VERSION)
}

@_cdecl("deview_init")
public func deviewInit(
    _ width: Int32,
    _ height: Int32,
    _ title: UnsafePointer<CChar>?,
    _ fontTtf: UnsafePointer<UInt8>?,
    _ fontLength: Int32,
    _ fontSize: Float,
    _ hidden: Int32) -> Int32 {
    autoreleasepool {
        initialise(width, height, title, fontTtf, fontLength, fontSize, hidden)
    }
}

private func initialise(
    _ width: Int32,
    _ height: Int32,
    _ title: UnsafePointer<CChar>?,
    _ fontTtf: UnsafePointer<UInt8>?,
    _ fontLength: Int32,
    _ fontSize: Float,
    _ hidden: Int32) -> Int32 {
    var font: Data?
    if let fontTtf, fontLength > 0 {
        font = Data(bytes: fontTtf, count: Int(fontLength))
    }

    let opened = Runtime.shared.open(
        width: width,
        height: height,
        title: title.map { String(cString: $0) } ?? "DiffEngineViewer",
        font: font,
        fontSize: CGFloat(fontSize),
        hidden: hidden != 0)
    return opened ? 1 : 0
}

@_cdecl("deview_present")
public func deviewPresent(_ screen: UnsafePointer<DeviewScreen>?) -> Int32 {
    let runtime = Runtime.shared
    guard runtime.initialised, let screen else {
        return 0
    }

    autoreleasepool {
        runtime.present(Frame.decode(screen))
    }

    return 1
}

@_cdecl("deview_poll_input")
public func deviewPollInput(_ input: UnsafeMutablePointer<DeviewInput>?) {
    guard let input else {
        return
    }

    let runtime = Runtime.shared
    autoreleasepool {
        if runtime.initialised {
            runtime.measureGrid()
        }
    }

    input.pointee = runtime.takeInput()
}

@_cdecl("deview_set_hidden")
public func deviewSetHidden(_ hidden: Int32) {
    autoreleasepool {
        if hidden == 0 {
            Runtime.shared.show()
        } else {
            Runtime.shared.hide()
        }
    }
}

@_cdecl("deview_set_clipboard")
public func deviewSetClipboard(_ text: UnsafePointer<CChar>?) {
    guard let text else {
        return
    }

    autoreleasepool {
        Clipboard.set(String(cString: text))
    }
}

@_cdecl("deview_focus")
public func deviewFocus() {
    autoreleasepool {
        Runtime.shared.show()
    }
}

@_cdecl("deview_set_placement")
public func deviewSetPlacement(_ placement: UnsafePointer<DeviewPlacement>?) {
    guard let placement else {
        return
    }

    Runtime.shared.placement = placement.pointee
}

@_cdecl("deview_get_placement")
public func deviewGetPlacement(_ placement: UnsafeMutablePointer<DeviewPlacement>?) -> Int32 {
    guard let placement else {
        return 0
    }

    return autoreleasepool { () -> Int32 in
        guard let now = Runtime.shared.placementNow() else {
            return 0
        }

        placement.pointee = now
        return 1
    }
}

@_cdecl("deview_shutdown")
public func deviewShutdown() {
    autoreleasepool {
        Runtime.shared.shutdown()
    }
}

/// Never called here: a logout's quit is the app's own (TerminateGuard), which reaches the clean
/// exit. Only the Linux shim can lose its display out from under it.
@_cdecl("deview_on_display_lost")
public func deviewOnDisplayLost(_ lost: DeviewDisplayLost?) {
}

/// Renders into a bitmap of this side's own making rather than asking the view for one, with
/// everything that varies pinned, so a committed baseline matches on any display: see `Capture`.
@_cdecl("deview_capture")
public func deviewCapture(
    _ screen: UnsafePointer<DeviewScreen>?,
    _ width: Int32,
    _ height: Int32,
    _ pngPath: UnsafePointer<CChar>?) -> Int32 {
    autoreleasepool {
        capture(screen, width, height, pngPath)
    }
}

private func capture(
    _ screen: UnsafePointer<DeviewScreen>?,
    _ width: Int32,
    _ height: Int32,
    _ pngPath: UnsafePointer<CChar>?) -> Int32 {
    guard let screen, let pngPath, let renderer = Runtime.shared.renderer else {
        return 0
    }

    let frame = Frame.decode(screen)
    let written = Capture.png(width: width, height: height, path: String(cString: pngPath)) { context in
        _ = renderer.draw(
            frame,
            in: context,
            size: CGSize(width: CGFloat(width), height: CGFloat(height)),
            capturing: true)
    }

    return written ? 1 : 0
}
