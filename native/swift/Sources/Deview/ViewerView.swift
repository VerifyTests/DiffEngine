import AppKit
import CDeview

/// The window's content. Drawing goes through the same `Renderer` the capture uses; this only adds
/// input, which it records into `Runtime` for the next `deview_poll_input` to drain.
final class ViewerView: NSView, NSViewToolTipOwner {
    private let renderer: Renderer
    private var draggingSplitter = false

    /// Where the last frame put things. Read by `Runtime` to anchor the context menu, which is a
    /// real `NSMenu` and so is popped from outside the drawing code.
    private(set) var layout = Renderer.Layout()

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

    /// One tip region per queue row that has something to say, rebuilt with the frame because
    /// anything that scrolls the column renumbers the rows. Driven from `Runtime` rather than from
    /// `draw`, since these are tracking rectangles and rebuilding them while AppKit is drawing
    /// invites re-entrancy.
    ///
    /// A row with an empty tooltip gets no region at all, rather than a region answering with an
    /// empty string: the second would still open a popup, and a popup that says nothing is worse
    /// than none.
    ///
    /// The text is answered on demand below rather than stored here, so a row whose label changed
    /// under a resting cursor still reads correctly.
    /// Rebuilt only when the regions themselves changed. AppKit times its tooltip delay from the
    /// moment the cursor enters a tracking rectangle, and this runs on every frame, so removing
    /// and re-adding the rectangle under a resting cursor restarted that delay before it could
    /// ever elapse - which is to say queue tooltips never appeared on macOS at all.
    func refreshToolTips() {
        let wanted = layout.queueItems.enumerated()
            .filter { $0.offset < model.queue.count && !model.queue[$0.offset].tooltip.isEmpty }
            .map(\.element)
        guard wanted != toolTipRects else {
            return
        }

        toolTipRects = wanted
        removeAllToolTips()
        for bounds in wanted {
            _ = addToolTip(bounds, owner: self, userData: nil)
        }
    }

    /// What the tips are registered on, so an unchanged frame can leave them alone.
    private var toolTipRects: [NSRect] = []

    /// Composed by the managed side, so this only finds the row under the cursor.
    func view(_ view: NSView, stringForToolTip tag: NSView.ToolTipTag, point: NSPoint, userData: UnsafeMutableRawPointer?) -> String {
        guard let index = layout.queueItems.firstIndex(where: { $0.contains(point) }),
              index < model.queue.count
        else {
            return ""
        }

        return model.queue[index].tooltip
    }

    /// The resize cursor over the splitter, which is the only hint that it can be dragged.
    override func resetCursorRects() {
        super.resetCursorRects()
        if !layout.splitter.isEmpty {
            addCursorRect(layout.splitter, cursor: .resizeLeftRight)
        }
    }

    override func mouseDown(with event: NSEvent) {
        // No menu hit test: the menu is an NSMenu, and while one is open it owns the mouse. A
        // click that dismisses it never reaches here, which is the platform's behaviour and the
        // reason the first click after a menu no longer also selects a row.
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

    override func rightMouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        if let index = layout.queueItems.firstIndex(where: { $0.contains(point) }),
           index < model.queue.count {
            Runtime.shared.input.rightClickedQueueItem = Int32(index)
            return
        }

        super.rightMouseDown(with: event)
    }

    /// A notch of a wheel, in the points a precise device reports one movement of it as.
    ///
    /// AppKit reports a wheel in lines and a trackpad in points, and rounding both to an integer
    /// number of notches treated them as the same thing: an ordinary flick of a trackpad reads as
    /// tens of points, so it arrived as tens of notches and the managed side then multiplied it
    /// by three. Slow movement rounded to nothing at all.
    private static let pointsPerNotch = 16.0

    /// What is left over between events, because a trackpad delivers many small deltas between
    /// two polls and dropping each one on its own is what made slow movement do nothing.
    private var scrollRemainder = 0.0

    override func scrollWheel(with event: NSEvent) {
        scrollRemainder += event.hasPreciseScrollingDeltas
            ? event.scrollingDeltaY / ViewerView.pointsPerNotch
            : event.scrollingDeltaY
        let notches = scrollRemainder.rounded(.towardZero)
        scrollRemainder -= notches
        if notches != 0 {
            Runtime.shared.input.scrollDelta += Int32(notches)
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
        case "v":
            return DEVIEW_KEY_NEXT_VARIANT.value
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
