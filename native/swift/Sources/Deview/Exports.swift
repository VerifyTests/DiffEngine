import AppKit
import CDeview
import CoreGraphics
import Foundation
import ImageIO

/// The nine entry points of native/include/deview.h, implemented over AppKit and Core Text.
///
/// The header is imported for its struct layouts only, with DEVIEW_TYPES_ONLY, so these are the
/// definitions of those symbols rather than a second declaration of them.

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

    runtime.present(Frame.decode(screen))
    return 1
}

@_cdecl("deview_poll_input")
public func deviewPollInput(_ input: UnsafeMutablePointer<DeviewInput>?) {
    guard let input else {
        return
    }

    let runtime = Runtime.shared
    if runtime.initialised {
        runtime.measureGrid()
    }

    input.pointee = runtime.input
    runtime.resetInput()
}

@_cdecl("deview_set_hidden")
public func deviewSetHidden(_ hidden: Int32) {
    if hidden == 0 {
        Runtime.shared.show()
    } else {
        Runtime.shared.hide()
    }
}

@_cdecl("deview_set_clipboard")
public func deviewSetClipboard(_ text: UnsafePointer<CChar>?) {
    guard let text else {
        return
    }

    // Cleared first: NSPasteboard keeps whatever types were declared before, so writing a string
    // over an image would otherwise leave both on the board and paste the wrong one.
    let board = NSPasteboard.general
    board.clearContents()
    board.setString(String(cString: text), forType: .string)
}

@_cdecl("deview_focus")
public func deviewFocus() {
    Runtime.shared.show()
}

@_cdecl("deview_shutdown")
public func deviewShutdown() {
    Runtime.shared.shutdown()
}

/// Renders into a bitmap of this side's own making rather than asking the view for one.
///
/// `bitmapImageRepForCachingDisplay` would inherit the window's backing scale, which is 2 on a
/// Retina machine and 1 elsewhere, so a committed baseline would only ever match on the kind of
/// display that produced it. Everything that varies is pinned here instead: scale, colour space,
/// and the six font smoothing and subpixel switches. No window is needed, which also means the
/// snapshot tests do not need a window server.
@_cdecl("deview_capture")
public func deviewCapture(
    _ screen: UnsafePointer<DeviewScreen>?,
    _ width: Int32,
    _ height: Int32,
    _ pngPath: UnsafePointer<CChar>?) -> Int32 {
    guard let screen,
          let pngPath,
          let renderer = Runtime.shared.renderer,
          let space = CGColorSpace(name: CGColorSpace.sRGB),
          let context = CGContext(
              data: nil,
              width: Int(width),
              height: Int(height),
              bitsPerComponent: 8,
              bytesPerRow: 0,
              space: space,
              bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)
    else {
        return 0
    }

    context.setAllowsFontSmoothing(false)
    context.setShouldSmoothFonts(false)
    context.setAllowsFontSubpixelPositioning(false)
    context.setShouldSubpixelPositionFonts(false)
    context.setAllowsFontSubpixelQuantization(false)
    context.setShouldSubpixelQuantizeFonts(false)

    renderer.draw(
        Frame.decode(screen),
        in: context,
        size: CGSize(width: CGFloat(width), height: CGFloat(height)))

    guard let image = context.makeImage() else {
        return 0
    }

    let url = URL(fileURLWithPath: String(cString: pngPath)) as CFURL
    guard let destination = CGImageDestinationCreateWithURL(url, "public.png" as CFString, 1, nil) else {
        return 0
    }

    CGImageDestinationAddImage(destination, image, nil)
    return CGImageDestinationFinalize(destination) ? 1 : 0
}
