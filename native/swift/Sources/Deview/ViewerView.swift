import AppKit
import CDeview

/// The window's content. Drawing goes through the same `Renderer` the capture uses; this only adds
/// input, which it records into `Runtime` for the next `deview_poll_input` to drain.
final class ViewerView: NSView, NSViewToolTipOwner {
    private let renderer: Renderer
    private var draggingSplitter = false

    /// Whether the left button is down over a pane, and where it went down. The side is fixed for
    /// the life of the drag: a selection belongs to one pane, so crossing into the other extends
    /// within the first rather than jumping.
    private var selecting = false
    private var selectSide: Int32 = 0
    private var selectAnchorRow: Int32 = 0
    private var selectAnchorColumn: Int32 = 0

    /// An enlarged picture being dragged about: where the button went down, and how the picture
    /// was placed then, which the whole drag is measured from. Measured from the last event
    /// instead, it would drift by whatever each frame's clamp took off it.
    private var panning = false
    private var panStart = NSPoint.zero
    private var panFrom = Renderer.PictureSpace()

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

    /// A move to a display of another scale changes the device pixels a picture fills, and so
    /// the scaled copy the renderer keeps of it, under a frame that has not changed.
    override func viewDidChangeBackingProperties() {
        super.viewDidChangeBackingProperties()
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let context = NSGraphicsContext.current?.cgContext else {
            return
        }

        let previous = layout.splitter
        let pictures = layout.pictures
        layout = renderer.draw(model, in: context, size: bounds.size)
        if layout.splitter != previous || layout.pictures != pictures {
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

        // The open hand over a picture that can be moved, which is the only thing that says it can
        for picture in layout.pictures where picture.enlarged {
            addCursorRect(picture.bounds, cursor: .openHand)
        }
    }

    override func mouseDown(with event: NSEvent) {
        // No menu hit test: the menu is an NSMenu, and while one is open it owns the mouse. A
        // click that dismisses it never reaches here, which is the platform's behaviour and the
        // reason the first click after a menu no longer also selects a row.
        let point = convert(event.locationInWindow, from: nil)

        // Control held makes it the click that asks for a menu, as it is everywhere on a Mac.
        // AppKit sees to that itself only for a view it can ask for an NSMenu, and this one's
        // menus are the managed side's, popped a frame later: with none to give, the click came
        // here as an ordinary press and selected a row or began a selection. Nothing else when
        // there is no menu under it, as a right click there does nothing.
        if event.modifierFlags.contains(.control) {
            _ = contextClick(at: point)
            return
        }

        if let index = layout.buttons.firstIndex(where: { $0.contains(point) }) {
            Runtime.shared.post(.button(Int32(index)))
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
            Runtime.shared.post(.queueItem(Int32(index)))
            return
        }

        // A picture enlarged past its space is taken hold of and moved. Before the text below it,
        // so the same press is not also the start of a selection in the rows above. One that fits
        // has nowhere to go, and a press on it is whatever it always was.
        if let picture = layout.pictures.first(where: { $0.enlarged && $0.bounds.contains(point) }) {
            panning = true
            panStart = point
            panFrom = picture
            return
        }

        // Not gated on there being a queue: file mode has two panes and no column, and its text is
        // as worth copying as anything else.
        guard let cell = paneCell(at: point) else {
            return
        }

        selecting = true
        selectSide = cell.side
        selectAnchorRow = cell.row
        selectAnchorColumn = cell.column
        // Both ends on the press, so a click with no drag behind it reports an empty selection,
        // which is what clears the previous one.
        report(focusRow: cell.row, focusColumn: cell.column)
    }

    override func mouseDragged(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        if draggingSplitter {
            renderer.dragQueueWidth(to: point.x, in: bounds.width)
            needsDisplay = true
            return
        }

        if panning {
            // Where it is rather than how far it moved, and already inside what the space can show:
            // the managed side holds one centre for both panes and knows nothing of points. A way
            // this pane's picture cannot move comes back as the frame had it, for the other pane.
            let centre = panFrom.dragged(
                by: CGSize(width: point.x - panStart.x, height: point.y - panStart.y))
            Runtime.shared.input.panX = Float(centre.x)
            Runtime.shared.input.panY = Float(centre.y)
            return
        }

        guard selecting else {
            super.mouseDragged(with: event)
            return
        }

        // Against the side the press landed in, whatever the pointer has wandered over since: a
        // selection is one pane's, and the other pane's rows are a different document.
        report(
            focusRow: draggedRow(point.y, side: selectSide),
            focusColumn: column(at: point.x, side: selectSide))
    }

    override func mouseUp(with event: NSEvent) {
        if draggingSplitter {
            draggingSplitter = false
            return
        }

        if panning {
            // Nothing to report, as for a selection: the last drag already said where it is.
            panning = false
            return
        }

        if selecting {
            // Nothing to report: the managed side is already holding the selection, so a release
            // has nothing left to say.
            selecting = false
            return
        }

        super.mouseUp(with: event)
    }

    /// The pane cell under a point, or nil when the point is not over one. Rows are rows of the
    /// whole side rather than of the visible slice, since that is what a selection is anchored in
    /// and only this side knows the scroll top the frame was drawn with.
    private func paneCell(at point: NSPoint) -> (side: Int32, row: Int32, column: Int32)? {
        guard layout.panes.count == 2,
              !layout.body.isEmpty,
              renderer.cell.height > 0,
              point.y >= layout.body.minY,
              point.y <= layout.body.maxY,
              point.x >= layout.panes[0].cellLeft,
              point.x <= layout.body.maxX
        else {
            return nil
        }

        let side: Int32 = point.x >= layout.panes[1].cellLeft ? 1 : 0
        return (side, draggedRow(point.y, side: side), column(at: point.x, side: side))
    }

    /// The row under a y, in rows of the whole side and clamped into the rows the pane drew, as
    /// Linux's `RowAt` does: a drag below the last row means the last row rather than nothing.
    /// Clamped to the body instead, a drag past the end reached the rows the managed side holds
    /// back below what it draws, so the status line counted, and a copy took, rows nobody saw
    /// highlighted.
    private func draggedRow(_ y: CGFloat, side: Int32) -> Int32 {
        let line = renderer.cell.height
        let capacity = max(1, Int(layout.body.height / line))
        let pane = side == 1 ? model.right : model.left
        let drawn = max(1, min(capacity, pane.rows.count))
        // The context is not flipped, so the top of the body is its maxY and rows count downwards
        // from there.
        let visible = min(max(Int((layout.body.maxY - y) / line), 0), drawn - 1)
        return pane.scrollTop + Int32(visible)
    }

    /// Rounded to the nearest boundary between characters rather than truncated to the one under
    /// the pointer, because a selection ends between two characters. Unclamped at the top: the
    /// managed side holds the text and pulls it back to the end of the line there.
    private func column(at x: CGFloat, side: Int32) -> Int32 {
        guard layout.panes.count == 2, renderer.cell.width > 0 else {
            return 0
        }

        let textLeft = layout.panes[Int(side)].textLeft
        return Int32(max(0, ((x - textLeft) / renderer.cell.width + 0.5).rounded(.down)))
    }

    private func report(focusRow: Int32, focusColumn: Int32) {
        Runtime.shared.input.dragSide = selectSide
        Runtime.shared.input.dragAnchorRow = selectAnchorRow
        Runtime.shared.input.dragAnchorColumn = selectAnchorColumn
        Runtime.shared.input.dragFocusRow = focusRow
        Runtime.shared.input.dragFocusColumn = focusColumn
    }

    override func rightMouseDown(with event: NSEvent) {
        if !contextClick(at: convert(event.locationInWindow, from: nil)) {
            super.rightMouseDown(with: event)
        }
    }

    /// A click that asks for a menu: the right button, or the left with control held. Says
    /// whether there was anything under it to ask one for.
    private func contextClick(at point: NSPoint) -> Bool {
        if let index = layout.queueItems.firstIndex(where: { $0.contains(point) }),
           index < model.queue.count {
            Runtime.shared.post(.rightClickedQueueItem(Int32(index)))
            return true
        }

        // Anywhere in a pane, its text or under it: the menu is the pane's, and a file of three
        // lines has most of its pane under them. The point is kept, because the menu is popped
        // where the click landed and the managed side is told only which pane.
        if layout.panes.count == 2,
           !layout.body.isEmpty,
           point.y >= layout.body.minY,
           point.y <= layout.body.maxY,
           point.x >= layout.panes[0].cellLeft,
           point.x <= layout.body.maxX {
            Runtime.shared.post(.rightClickedPane(point.x >= layout.panes[1].cellLeft ? 1 : 0))
            Runtime.shared.paneMenuPoint = point
            return true
        }

        return false
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
        let notches: Double
        if event.hasPreciseScrollingDeltas {
            scrollRemainder += Double(event.scrollingDeltaY) / ViewerView.pointsPerNotch
            notches = scrollRemainder.rounded(.towardZero)
            scrollRemainder -= notches
        } else {
            // A wheel with notches: an event is a click of it. Its delta is in lines, scaled by
            // how fast the wheel is turning, and for one click turned slowly that is a tenth of
            // a line. Added up as a trackpad's are, ten such clicks went by before anything
            // moved. So at least one notch the way it turned, and more only when it says more.
            let lines = Double(event.scrollingDeltaY).rounded()
            if event.scrollingDeltaY > 0 {
                notches = max(1, lines)
            } else if event.scrollingDeltaY < 0 {
                notches = min(-1, lines)
            } else {
                // Turned sideways, which nothing here answers
                notches = 0
            }

            scrollRemainder = 0
        }

        guard notches != 0 else {
            return
        }

        // Over a picture the wheel is for the picture, and anywhere with command or control held,
        // as it is in everything else that shows one. Everywhere else it scrolls the rows, as it
        // always has. Decided here because only this side knows what the pointer was over.
        let point = convert(event.locationInWindow, from: nil)
        let modified = event.modifierFlags.contains(.command) || event.modifierFlags.contains(.control)
        if modified || layout.pictures.contains(where: { $0.bounds.contains(point) }) {
            Runtime.shared.input.zoomDelta += Int32(notches)
        } else {
            Runtime.shared.input.scrollDelta += Int32(notches)
        }
    }

    /// How far two fingers have to spread or close for one step of zoom.
    private static let magnificationPerStep = 0.25

    private var magnifyRemainder = 0.0

    /// A pinch, which is how a trackpad says zoom. Steps, like the wheel, so every way of zooming
    /// moves between the same sizes.
    override func magnify(with event: NSEvent) {
        magnifyRemainder += Double(event.magnification) / ViewerView.magnificationPerStep
        let steps = magnifyRemainder.rounded(.towardZero)
        magnifyRemainder -= steps
        if steps != 0 {
            Runtime.shared.input.zoomDelta += Int32(steps)
        }
    }

    override func keyDown(with event: NSEvent) {
        let key = ViewerView.map(event)
        if key == DEVIEW_KEY_NONE.value {
            super.keyDown(with: event)
            return
        }

        // A key held past the repeat delay arrives again many times a second, and each one is
        // queued. That is what a held Down is for. A held a accepted the entry on screen and then
        // every one that took its place, into source, none of them read, and the ones queued
        // while a frame was slow were still handed over after the key came up. So what changes
        // the queue takes a press each, as it does in the WinForms head. Swallowed rather than
        // passed on: it is still this key.
        if event.isARepeat, ViewerView.changesQueue(key) {
            return
        }

        Runtime.shared.post(.key(key))
    }

    /// The keys that act on the queue rather than on the view: the ones of
    /// `ViewerSession.ChangesQueue` that a key here can be.
    private static func changesQueue(_ key: Int32) -> Bool {
        key == DEVIEW_KEY_ACCEPT.value ||
            key == DEVIEW_KEY_ACCEPT_ALL.value ||
            key == DEVIEW_KEY_DISCARD.value
    }

    /// Matches ReadKey in deview.cpp and the WinForms head's Map, which is the keymap the docs
    /// publish.
    private static func map(_ event: NSEvent) -> Int32 {
        let shift = event.modifierFlags.contains(.shift)
        // Command normally never reaches here, because the Edit menu's key equivalents are matched
        // first. Control is the fallback for a keyboard driving this over a remote session, and
        // both are answered before the plain letters below: without that, ctrl+a fell through to
        // A, which accepts.
        if event.modifierFlags.contains(.command) || event.modifierFlags.contains(.control) {
            switch event.charactersIgnoringModifiers?.lowercased() {
            case "c":
                return DEVIEW_KEY_COPY.value
            case "a":
                return DEVIEW_KEY_SELECT_ALL.value
            // With command or control as well as without, since that is the chord everything else
            // that zooms taught
            case "=", "+":
                return DEVIEW_KEY_ZOOM_IN.value
            case "-":
                return DEVIEW_KEY_ZOOM_OUT.value
            case "0":
                return DEVIEW_KEY_ZOOM_RESET.value
            default:
                return DEVIEW_KEY_NONE.value
            }
        }

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

        // The brackets by what was typed, before the letters below are matched by which key it
        // was. German, French, Nordic, Spanish and Italian layouts have them behind Option, and
        // with the modifiers left out the key is the digit or the letter printed on it, so those
        // readers could not turn a page from the keyboard.
        //
        // The zoom keys the same way, for a layout that has one of them behind Option. Shift is
        // no part of this: it is kept by both readings, so equals unshifted and plus with shift
        // held are each what was typed on a US keyboard, and each zooms in, as they did.
        switch event.characters {
        case "[":
            return DEVIEW_KEY_PREVIOUS_PAGE.value
        case "]":
            return DEVIEW_KEY_NEXT_PAGE.value
        case "=", "+":
            return DEVIEW_KEY_ZOOM_IN.value
        case "-":
            return DEVIEW_KEY_ZOOM_OUT.value
        default:
            break
        }

        switch event.charactersIgnoringModifiers?.lowercased() {
        case "n":
            return DEVIEW_KEY_NEXT_CHANGE.value
        case "p":
            return DEVIEW_KEY_PREVIOUS_CHANGE.value
        case "m":
            return DEVIEW_KEY_TOGGLE_MINIMAL.value
        case "r":
            return DEVIEW_KEY_TOGGLE_DRAWING.value
        case "j":
            return DEVIEW_KEY_NEXT_PROJECTION.value
        // Plus is the equals key whether or not shift is held: nobody reaches for shift to zoom in
        case "=", "+":
            return DEVIEW_KEY_ZOOM_IN.value
        case "-":
            return DEVIEW_KEY_ZOOM_OUT.value
        case "0":
            return DEVIEW_KEY_ZOOM_RESET.value
        case "[":
            return DEVIEW_KEY_PREVIOUS_PAGE.value
        case "]":
            return DEVIEW_KEY_NEXT_PAGE.value
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

/// The pane scrollbar, with a drag of its knob followed here rather than in AppKit's own loop.
///
/// `NSScroller` tracks a press on its knob in a loop of its own, inside `mouseDown`, and returns
/// when the button comes up. That is inside `Runtime.pump`, so `deview_present` did not return
/// for the length of the drag: the knob moved under the pointer, every move was reported, and the
/// managed loop that scrolls the panes in answer ran once, at the release. The WinForms head has
/// the same loop under its scroll bar and is handed frames from inside it, which this ABI has no
/// way to ask for.
///
/// So the drag is three ordinary events instead, as a selection is in `ViewerView`. The window
/// sends the moves and the release to the view that took the press, each comes through the pump
/// on its own, and there is a frame between one and the next.
///
/// Only a press on the knob. One in the slot is still AppKit's: what it means is the reader's
/// setting, a page or the place that was pressed, and it is over in a click.
final class PaneScroller: NSScroller {
    /// A knob being dragged: where in the window the pointer took hold of it, where the knob was
    /// then, as the fraction of its travel `doubleValue` is, and how long that travel is.
    private var dragging = false
    private var heldAt: CGFloat = 0
    private var heldValue = 0.0
    private var travel: CGFloat = 0

    override func mouseDown(with event: NSEvent) {
        let knob = rect(for: .knob)
        let room = rect(for: .knobSlot).height - knob.height
        guard room > 0, knob.contains(convert(event.locationInWindow, from: nil)) else {
            super.mouseDown(with: event)
            return
        }

        dragging = true
        heldAt = event.locationInWindow.y
        heldValue = doubleValue
        travel = room
    }

    override func mouseDragged(with event: NSEvent) {
        guard dragging else {
            super.mouseDragged(with: event)
            return
        }

        // Measured from the press rather than from the last move, as a picture's drag is, and in
        // the window's coordinates, which count up the screen while the document runs down it.
        // The knob is not moved here. It goes where the frame that answers this puts it, which
        // is the row the panes are showing.
        let moved = Double((heldAt - event.locationInWindow.y) / travel)
        Runtime.shared.knobDragged(to: min(max(heldValue + moved, 0), 1))
    }

    override func mouseUp(with event: NSEvent) {
        guard dragging else {
            super.mouseUp(with: event)
            return
        }

        // Nothing to report: the last move already said where it is.
        dragging = false
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
