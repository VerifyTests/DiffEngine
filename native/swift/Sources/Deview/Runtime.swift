import AppKit
import CDeview
import Foundation
import PlatformHeadsMac

/// Process wide, because the ABI is: one window, addressed by free functions.
///
/// Everything here runs on the thread that calls in, which is the managed side's main thread and
/// therefore the process main thread. AppKit requires that, and it is also why the loop stays in
/// C# rather than being inverted into `NSApplication.run`.
final class Runtime {
    static let shared = Runtime()

    private var delegate: WindowDelegate?

    /// A quit from the Dock or a logout, reported as a close, which the managed side answers the
    /// way it answers the window's close button: it leaves the loop, persists the queue it owns,
    /// and exits. macOS has no tray to hold the queue instead. Held here because
    /// `NSApplication.delegate` does not keep it alive.
    private lazy var terminateGuard = TerminateGuard { [unowned self] in
        self.input.closeRequested = 1
    }

    private let napGuard = NapGuard()
    private var size = CGSize(width: 1100, height: 700)
    private var title = "DiffEngineViewer"

    /// What every AppKit control here sends to. Held for the process, because a menu item's target
    /// is a weak reference and an autoreleased one would leave the menu inert.
    private let target = ControlTarget()

    private var scroller: KnobScroller?
    private var scrollerWidth: CGFloat = 0

    /// Whether the menu the current frame carries has already been popped. The managed side takes
    /// a frame to notice it was dismissed, and without this the popup would come straight back up
    /// in the meantime.
    private var menuShown = false

    /// When a spinner was last turned, and how often one is: often enough to read as turning, and
    /// no more, since each step is a redraw.
    private var lastSpin: TimeInterval = 0
    private static let spinStep: TimeInterval = 0.04

    var window: NSWindow?
    var view: ViewerView?
    var renderer: Renderer?
    var input = DeviewInput()
    var initialised = false

    /// Keys, clicks and menu events, in the order they happened, one handed over per poll.
    ///
    /// A slot per kind used to hold them, in `input`, and each handler overwrote its slot. `pump`
    /// dispatches everything AppKit has queued before it returns, so with a slow frame - the loop
    /// waiting behind an accept on InlineApplier's mutex - two presses of Down scrolled once, Tab
    /// then a accepted the entry the reader meant to skip, and d then a click on another row
    /// discarded the clicked one, which the reader had never looked at: the managed side applies
    /// a frame's click before its key. The WinForms head queues them for the same reason.
    ///
    /// Only these. The wheel adds up, a drag and the scroller say where they are now and a close
    /// is a flag, so each of those is whole however many events made it, and stays in `input`.
    private var discrete: [Discrete] = []

    enum Discrete {
        case key(Int32)
        case button(Int32)
        case queueItem(Int32)
        case rightClickedQueueItem(Int32)
        case rightClickedPane(Int32)
        case menuItem(Int32)
        case menuClosed
    }

    /// How the last window was left, handed over before this one exists and used as it is made,
    /// from the top left of the primary screen as every head measures it.
    var placement: DeviewPlacement?

    /// Where the right-click that asked for a pane's menu landed, in the view's coordinates, which
    /// is where the menu is popped.
    var paneMenuPoint = CGPoint.zero

    private init() {
        resetInput()
    }

    func open(width: Int32, height: Int32, title: String, font: Data?, fontSize: CGFloat, hidden: Bool) -> Bool {
        if initialised {
            return true
        }

        renderer = Renderer(fontData: font, size: fontSize)
        size = CGSize(width: CGFloat(width), height: CGFloat(height))
        self.title = title
        initialised = true

        // Out of App Nap for as long as this runs: covered, it could be slow to answer a failing
        // test's inline send, and hidden while it owns the queue it still has to answer every
        // send promptly.
        napGuard.begin(reason: "Reviewing snapshots and answering the queue")

        // A hidden start is capture only, and capture draws into a bitmap of its own making. Not
        // touching AppKit at all in that case is what lets the pixel tests run: NSWindow may only
        // be instantiated on the main thread, and a test host runs them on whatever thread it
        // likes. The app itself always starts visible, from Main, which is the main thread.
        // show() rather than makeWindow(), because a window that is built but never ordered
        // front is a Dock icon with nothing under it: the app would then present at 60 fps into
        // an invisible window until a second patch arrived over the socket and drove
        // deview_set_hidden/deview_focus, which is the only other path to makeKeyAndOrderFront.
        if !hidden {
            show()
        }

        measureGrid()
        return true
    }

    private func makeWindow() {
        guard window == nil, let renderer else {
            return
        }

        // Everything the renderer draws is dark, and everything AppKit draws here — the menus, the
        // tooltips, the scroller, the title bar — would otherwise follow the machine's setting and
        // come up light against it. The WinForms head says the same thing with SetColorMode.
        NSApplication.shared.appearance = NSAppearance(named: .darkAqua)
        HeadWindow.launch(menu: MainMenu.build(target), delegate: terminateGuard)

        let view = ViewerView(renderer: renderer, frame: NSRect(origin: .zero, size: size))
        let delegate = WindowDelegate()
        let window = HeadWindow.make(view: view, size: size, title: title, delegate: delegate)
        // Where the last window was, when its title bar is still on a screen there is now.
        // Displays come and go between runs, and a window opened where one used to be is a viewer
        // that looks as if it did not start. A zoomed window is only a frame here, so there is
        // nothing separate to restore.
        if let placement {
            WindowPlacement.restore(
                window,
                to: WindowBounds(x: placement.x, y: placement.y, width: placement.width, height: placement.height))
        }

        self.view = view
        self.window = window
        self.delegate = delegate
        makeScroller(in: view, renderer)
    }

    /// The window's frame as it is now, or nil when there is no window, or it is full screen.
    func placementNow() -> DeviewPlacement? {
        WindowPlacement.current(of: window).map {
            DeviewPlacement(x: $0.x, y: $0.y, width: $0.width, height: $0.height, maximized: 0)
        }
    }

    /// The pane scrollbar, a strip of its own beside the right hand pane, whose knob is followed
    /// as it is dragged: see `KnobScroller`.
    ///
    /// The strip comes off the renderer rather than out of the window, so the offscreen capture —
    /// which has no window and so no scroller — is not left with a gap where one would be.
    private func makeScroller(in view: ViewerView, _ renderer: Renderer) {
        let scroller = KnobScroller.legacy(in: view, target: target, action: #selector(ControlTarget.scrolled(_:)))
        scroller.knobStyle = .light
        scroller.knobDragged = { [unowned self] in
            self.knobDragged(to: $0)
        }

        self.scroller = scroller
        let width = scroller.frame.width
        scrollerWidth = width
        renderer.rightInset = width
    }

    func present(_ frame: Frame) {
        // Nothing to present when this runtime never took a window. Capture goes straight to the
        // renderer, so a headless one is still useful.
        guard let view else {
            return
        }

        // Drawn only when there is something new to draw. The managed loop presents at 60 fps
        // whether or not anything changed, and redrawing the whole window every time kept a core
        // busy for a viewer nobody was touching. What is on screen is the frame plus the pictures
        // it names, whose files can be rewritten under an unchanged frame and which are decoded
        // off this thread, landing between frames. Anything else - a resize, a splitter drag, a
        // move to a display of another scale - is invalidated by AppKit or by the view as it
        // happens.
        let landed = renderer?.takeFinished() == true
        if view.model != frame || landed || renderer?.picturesChanged(frame) == true {
            view.model = frame
            view.needsDisplay = true
        } else if !view.layout.spinners.isEmpty {
            // A spinner turns while nothing else changes, which is the whole time a picture is on
            // its way. Only where one is, so turning it does not redraw the whole window.
            let now = ProcessInfo.processInfo.systemUptime
            if now - lastSpin >= Runtime.spinStep {
                lastSpin = now
                for spinner in view.layout.spinners {
                    view.setNeedsDisplay(spinner)
                }
            }
        }

        view.displayIfNeeded()
        // After drawing, because both read where the last frame put the queue rows.
        view.refreshToolTips()
        position(frame)
        pump()
        // Last, because it blocks: a popped menu runs its own tracking loop and does not return
        // until the user has chosen or dismissed. That is the platform's behaviour and it is what
        // buys the keyboard, Escape and VoiceOver; the managed loop simply waits.
        popMenu(frame)
        measureGrid()
    }

    /// Puts the scroller beside the body and tells it where in the document that body sits.
    ///
    /// Counted in rows, so its travel is exactly the clamp the managed side applies. The rows on
    /// screen come from the slice it sent: that is shorter than the viewport only when the scroll
    /// top is past the clamp, which never happens, so it is the viewport in every reachable state
    /// and is the whole document when the document fits.
    private func position(_ frame: Frame) {
        guard let view, let scroller else {
            return
        }

        let body = view.layout.body
        scroller.frame = NSRect(
            x: view.bounds.maxX - scrollerWidth,
            y: body.minY,
            width: scrollerWidth,
            height: max(1, body.height))

        // Assigned during a drag of the knob as at any other time. `KnobScroller` reports where a
        // drag has got to without moving the knob, so this is what moves it: to the row the panes
        // are showing, which is where the WinForms bar's thumb sits too.
        Runtime.travel(of: frame).apply(to: scroller)
    }

    /// The panes' rows as the scroller counts them.
    private static func travel(of frame: Frame) -> ScrollerTravel {
        ScrollerTravel(top: Int(frame.left.scrollTop), visible: frame.left.rows.count, total: Int(frame.left.totalRows))
    }

    /// The knob dragged to `value` of the way along its travel, as a first visible row. From
    /// `KnobScroller`, which follows that drag itself.
    func knobDragged(to value: Double) {
        guard let frame = view?.model else {
            return
        }

        input.scrollTo = Runtime.travel(of: frame).row(at: value)
    }

    /// Translates wherever the scroller was grabbed into a first visible row. What reaches here is
    /// what AppKit tracked itself, which is a press in the slot.
    func scrolled(_ scroller: NSScroller) {
        guard let frame = view?.model else {
            return
        }

        input.scrollTo = Runtime.travel(of: frame).row(for: scroller.hitPart, value: scroller.doubleValue)
    }

    /// The context menu, as a real one. The managed side still owns opening and closing; this pops
    /// what the frame carries and reports what came back.
    private func popMenu(_ frame: Frame) {
        guard !frame.menu.isEmpty else {
            // Dropped by the managed side, so the next one may be shown.
            menuShown = false
            return
        }

        // Not while anything is waiting to be handed over. It happened before this menu could be
        // shown, and popping holds the managed loop for as long as the menu is up: a key pressed
        // straight after the right-click would be applied when the menu closed, however much later
        // that was. Handed over first, it is applied now, and a menu that outlives it is popped on
        // the frame after.
        guard !menuShown, discrete.isEmpty, let view else {
            return
        }

        // A pane's menu hangs where the pointer was when it was asked for, which the managed side
        // never knew. A queue row's hangs under the row.
        let at: CGPoint
        if frame.menuPane >= 0 {
            at = paneMenuPoint
        } else if frame.menuRow >= 0, Int(frame.menuRow) < view.layout.queueItems.count {
            // Nothing is flipped, so a row's minY is its bottom edge and a menu placed there hangs
            // below it. AppKit flips the whole thing near an edge of the screen, which is the
            // drawn one's other failing.
            let anchor = view.layout.queueItems[Int(frame.menuRow)]
            at = CGPoint(x: anchor.minX, y: anchor.minY)
        } else {
            return
        }

        menuShown = true
        let menu = NSMenu()
        for (index, label) in frame.menu.enumerated() {
            let item = NSMenuItem(
                title: label,
                action: #selector(ControlTarget.contextItem(_:)),
                keyEquivalent: "")
            item.target = target
            item.tag = index
            menu.addItem(item)
        }

        if !menu.popUp(positioning: nil, at: at, in: view) {
            // Escape, a click elsewhere, or focus lost. The click that did it was swallowed by the
            // tracking loop, so this is the only way the managed side can hear about it.
            post(.menuClosed)
        }
    }

    /// Drains what is queued and waits out the frame, returning early for input and waiting longer
    /// for a window nobody can see: see `EventPump`. A patch arriving over the socket is the
    /// managed side's reason to show the window, and `EventPump.unseenWait` is how late that can
    /// be.
    private func pump() {
        EventPump.pump(unseen: unseen) { hasInput }
    }

    private var unseen: Bool {
        EventPump.isUnseen(window)
    }

    /// Something is waiting for the next poll to hand over: a key or a click in line, or any of
    /// what `input` gathers, by the values `resetInput` clears each to.
    private var hasInput: Bool {
        !discrete.isEmpty ||
            input.scrollDelta != 0 ||
            input.zoomDelta != 0 ||
            input.scrollTo >= 0 ||
            input.dragSide >= 0 ||
            input.panX >= 0 ||
            input.closeRequested != 0
    }

    func measureGrid() {
        guard let renderer else {
            return
        }

        // The requested size when there is no view to ask, which is the headless capture case.
        let grid = renderer.grid(for: view?.bounds.size ?? size)
        input.columns = grid.columns
        input.rows = grid.rows
    }

    /// Presents the window, building it first if this runtime started headless, so a hidden start
    /// is still only a deferral rather than a different contract from the other heads. Also how a
    /// visible `open` presents, so ordering front happens in exactly one place. Only reachable
    /// from the managed loop's thread, which is the main one.
    func show() {
        makeWindow()
        HeadWindow.show(window)
    }

    func hide() {
        window?.orderOut(nil)
    }

    func shutdown() {
        napGuard.end()
        HeadWindow.close(window)
        window = nil
        view = nil
        renderer = nil
        delegate = nil
        scroller = nil
        menuShown = false
        discrete = []
        initialised = false
    }

    /// Records a key, a click or a menu event, behind whatever is already waiting.
    func post(_ event: Discrete) {
        discrete.append(event)
    }

    /// What one poll hands over: everything in `input`, and the discrete event that is next in
    /// line when there is one. Only the one, so the managed side applies them in the order they
    /// happened rather than in the order it reads a frame's fields.
    func takeInput() -> DeviewInput {
        var taken = input
        resetInput()
        // As it is now, like the grid: what slows the pump is said to the managed side as well,
        // which slows what it keeps going beside a window for whoever is reading it.
        taken.unseen = unseen ? 1 : 0
        guard !discrete.isEmpty else {
            return taken
        }

        switch discrete.removeFirst() {
        case let .key(key):
            taken.key = key
        case let .button(index):
            taken.clickedButton = index
        case let .queueItem(index):
            taken.clickedQueueItem = index
        case let .rightClickedQueueItem(index):
            taken.rightClickedQueueItem = index
        case let .rightClickedPane(side):
            taken.rightClickedPane = side
        case let .menuItem(index):
            taken.clickedMenuItem = index
        case .menuClosed:
            taken.menuClosed = 1
        }

        return taken
    }

    /// Each event is delivered exactly once, so the poll that read them clears them. The grid is
    /// left alone: it is a state, not an event.
    func resetInput() {
        input.key = DEVIEW_KEY_NONE.value
        input.clickedButton = -1
        input.clickedQueueItem = -1
        input.rightClickedQueueItem = -1
        input.clickedMenuItem = -1
        input.menuClosed = 0
        input.scrollDelta = 0
        // -1, not 0: zero is a legitimate scroll target, so a cleared field has to mean "no
        // target" rather than "go to the top".
        input.scrollTo = -1
        input.closeRequested = 0
        // -1 for the same reason scrollTo is: 0 is the left pane, so a cleared field has to say
        // "no drag" rather than "a drag in the left pane at row 0".
        input.dragSide = -1
        input.dragAnchorRow = 0
        input.dragAnchorColumn = 0
        input.dragFocusRow = 0
        input.dragFocusColumn = 0
        input.zoomDelta = 0
        // -1 again: 0 is the left edge of a picture, so a cleared field has to say "no drag".
        input.panX = -1
        input.panY = -1
        input.rightClickedPane = -1
    }
}
