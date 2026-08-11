import AppKit
import CDeview

/// The window's content. Drawing goes through the same `Renderer` the capture uses; this only adds
/// input, which it records into `Runtime` for the next `deview_poll_input` to drain.
final class ViewerView: NSView {
    private let renderer: Renderer
    private var layout = Renderer.Layout()
    private var draggingSplitter = false

    var model = Frame()

    init(renderer: Renderer, frame: NSRect) {
        self.renderer = renderer
        super.init(frame: frame)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("Not loaded from a nib.")
    }

    /// Left as false so the view's context matches the offscreen one, which lets both go through
    /// the same drawing code.
    override var isFlipped: Bool { false }

    override var acceptsFirstResponder: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        guard let context = NSGraphicsContext.current?.cgContext else {
            return
        }

        let previous = layout.splitter
        layout = renderer.draw(model, in: context, size: bounds.size)
        if layout.splitter != previous {
            window?.invalidateCursorRects(for: self)
        }
    }

    /// The resize cursor over the splitter, which is the only hint that it can be dragged.
    override func resetCursorRects() {
        super.resetCursorRects()
        if !layout.splitter.isEmpty {
            addCursorRect(layout.splitter, cursor: .resizeLeftRight)
        }
    }

    override func mouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        if let index = layout.buttons.firstIndex(where: { $0.contains(point) }) {
            Runtime.shared.input.clickedButton = Int32(index)
            return
        }

        // Before the queue hit test, because the grab zone overlaps the right edge of the column
        // and a drag that started there would otherwise also select whatever it began over.
        if layout.splitter.contains(point) {
            draggingSplitter = true
            return
        }

        if let index = layout.queueItems.firstIndex(where: { $0.contains(point) }),
           index < model.queue.count {
            Runtime.shared.input.clickedQueueItem = Int32(index)
        }
    }

    override func mouseDragged(with event: NSEvent) {
        guard draggingSplitter else {
            super.mouseDragged(with: event)
            return
        }

        renderer.dragQueueWidth(to: convert(event.locationInWindow, from: nil).x, in: bounds.width)
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        if draggingSplitter {
            draggingSplitter = false
            return
        }

        super.mouseUp(with: event)
    }

    /// Accumulated, because a trackpad delivers many small deltas between two polls and the
    /// managed side amplifies whatever it is given.
    override func scrollWheel(with event: NSEvent) {
        let notches = Int32(event.scrollingDeltaY.rounded())
        if notches != 0 {
            Runtime.shared.input.scrollDelta += notches
        }
    }

    override func keyDown(with event: NSEvent) {
        let key = ViewerView.map(event)
        if key == DEVIEW_KEY_NONE.value {
            super.keyDown(with: event)
            return
        }

        Runtime.shared.input.key = key
    }

    /// Matches ReadKey in deview.cpp and the WinForms head's Map, which is the keymap the docs
    /// publish.
    private static func map(_ event: NSEvent) -> Int32 {
        let shift = event.modifierFlags.contains(.shift)
        switch Int(event.keyCode) {
        case 126:
            return DEVIEW_KEY_SCROLL_UP.value
        case 125:
            return DEVIEW_KEY_SCROLL_DOWN.value
        case 116:
            return DEVIEW_KEY_PAGE_UP.value
        case 121:
            return DEVIEW_KEY_PAGE_DOWN.value
        case 115:
            return DEVIEW_KEY_HOME.value
        case 119:
            return DEVIEW_KEY_END.value
        case 48:
            return shift ? DEVIEW_KEY_PREVIOUS_ITEM.value : DEVIEW_KEY_NEXT_ITEM.value
        case 53:
            return DEVIEW_KEY_QUIT.value
        default:
            break
        }

        switch event.charactersIgnoringModifiers?.lowercased() {
        case "n":
            return DEVIEW_KEY_NEXT_CHANGE.value
        case "p":
            return DEVIEW_KEY_PREVIOUS_CHANGE.value
        case "a":
            return shift ? DEVIEW_KEY_ACCEPT_ALL.value : DEVIEW_KEY_ACCEPT.value
        case "d":
            return DEVIEW_KEY_DISCARD.value
        case "q":
            return DEVIEW_KEY_QUIT.value
        default:
            return DEVIEW_KEY_NONE.value
        }
    }
}

/// Closing is the managed side's decision: with a tray to reopen from it hides, without one it
/// exits. So the request is recorded and the close refused, and the answer comes back as either
/// `deview_set_hidden` or `deview_shutdown`.
final class WindowDelegate: NSObject, NSWindowDelegate {
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        Runtime.shared.input.closeRequested = 1
        return false
    }
}
