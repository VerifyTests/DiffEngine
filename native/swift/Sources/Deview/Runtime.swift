import AppKit
import CDeview
import Foundation

/// Process wide, because the ABI is: one window, addressed by free functions.
///
/// Everything here runs on the thread that calls in, which is the managed side's main thread and
/// therefore the process main thread. AppKit requires that, and it is also why the loop stays in
/// C# rather than being inverted into `NSApplication.run`.
final class Runtime {
    static let shared = Runtime()

    private var delegate: WindowDelegate?
    private var size = CGSize(width: 1100, height: 700)
    private var title = "DiffEngineViewer"

    /// What every AppKit control here sends to. Held for the process, because a menu item's target
    /// is a weak reference and an autoreleased one would leave the menu inert.
    private let target = ControlTarget()

    private var scroller: NSScroller?
    private var scrollerWidth: CGFloat = 0

    /// Whether the menu the current frame carries has already been popped. The managed side takes
    /// a frame to notice it was dismissed, and without this the popup would come straight back up
    /// in the meantime.
    private var menuShown = false

    var window: NSWindow?
    var view: ViewerView?
    var renderer: Renderer?
    var input = DeviewInput()
    var initialised = false

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

        // A hidden start is capture only, and capture draws into a bitmap of its own making. Not
        // touching AppKit at all in that case is what lets the pixel tests run: NSWindow may only
        // be instantiated on the main thread, and a test host runs them on whatever thread it
        // likes. The app itself always starts visible, from Main, which is the main thread.
        if !hidden {
            makeWindow()
        }

        measureGrid()
        return true
    }

    private func makeWindow() {
        guard window == nil, let renderer else {
            return
        }

        let application = NSApplication.shared
        // Regular rather than accessory, so the window can take focus and appear in the dock
        // without this being an app bundle. finishLaunching is the part of run() that has to
        // happen before events are pumped by hand.
        application.setActivationPolicy(.regular)

        // Everything the renderer draws is dark, and everything AppKit draws here — the menus, the
        // tooltips, the scroller, the title bar — would otherwise follow the machine's setting and
        // come up light against it. The WinForms head says the same thing with SetColorMode.
        application.appearance = NSAppearance(named: .darkAqua)

        // Before finishLaunching, which is when the bar is first read.
        application.mainMenu = MainMenu.build(target)
        application.finishLaunching()

        let bounds = NSRect(origin: .zero, size: size)
        let view = ViewerView(renderer: renderer, frame: bounds)
        let window = NSWindow(
            contentRect: bounds,
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false)
        let delegate = WindowDelegate()

        window.title = title
        window.contentView = view
        window.delegate = delegate
        window.isReleasedWhenClosed = false
        window.center()

        self.view = view
        self.window = window
        self.delegate = delegate
        makeScroller(in: view, renderer)
    }

    /// The pane scrollbar.
    ///
    /// Legacy rather than overlay, whatever the machine prefers. An overlay scroller only fades in
    /// and out as part of an `NSScrollView`, and there is none here — the managed side owns the
    /// scroll position and hands over one screenful of rows at a time. Placed by hand it would
    /// simply sit over the right hand pane forever. Legacy takes a strip of its own instead, which
    /// is also what the WinForms head does and for the same reason: a bar that comes and goes
    /// moves the pane split about.
    ///
    /// The strip comes off the renderer rather than out of the window, so the offscreen capture —
    /// which has no window and so no scroller — is not left with a gap where one would be.
    private func makeScroller(in view: ViewerView, _ renderer: Renderer) {
        let width = NSScroller.scrollerWidth(for: .regular, scrollerStyle: .legacy)
        let scroller = NSScroller(frame: NSRect(x: 0, y: 0, width: width, height: view.bounds.height))
        scroller.scrollerStyle = .legacy
        scroller.knobStyle = .light
        scroller.target = target
        scroller.action = #selector(ControlTarget.scrolled(_:))
        view.addSubview(scroller)

        self.scroller = scroller
        scrollerWidth = width
        renderer.rightInset = width
    }

    func present(_ frame: Frame) {
        // Nothing to present when this runtime never took a window. Capture goes straight to the
        // renderer, so a headless one is still useful.
        guard let view else {
            return
        }

        view.model = frame
        view.needsDisplay = true
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

        // Assigned rather than guarded against a drag in progress, because there cannot be one:
        // a legacy scroller tracks in a loop of its own, inside the pump this runs before.
        let visible = max(1, frame.left.rows.count)
        let total = max(Int(frame.left.totalRows), visible)
        let maximum = total - visible
        scroller.knobProportion = CGFloat(visible) / CGFloat(total)
        scroller.doubleValue = maximum <= 0 ? 0 : Double(frame.left.scrollTop) / Double(maximum)
    }

    /// Translates wherever the scroller was grabbed into a first visible row.
    func scrolled(_ scroller: NSScroller) {
        guard let frame = view?.model else {
            return
        }

        let visible = max(1, frame.left.rows.count)
        let maximum = max(0, Int(frame.left.totalRows) - visible)
        switch scroller.hitPart {
        case .decrementPage:
            input.scrollTo = Int32(max(0, Int(frame.left.scrollTop) - visible))
        case .incrementPage:
            input.scrollTo = Int32(min(maximum, Int(frame.left.scrollTop) + visible))
        case .decrementLine:
            input.scrollTo = Int32(max(0, Int(frame.left.scrollTop) - 1))
        case .incrementLine:
            input.scrollTo = Int32(min(maximum, Int(frame.left.scrollTop) + 1))
        default:
            input.scrollTo = Int32((scroller.doubleValue * Double(maximum)).rounded())
        }
    }

    /// The context menu, as a real one. The managed side still owns opening and closing; this pops
    /// what the frame carries and reports what came back.
    private func popMenu(_ frame: Frame) {
        guard !frame.menu.isEmpty else {
            // Dropped by the managed side, so the next one may be shown.
            menuShown = false
            return
        }

        guard !menuShown,
              let view,
              frame.menuRow >= 0,
              Int(frame.menuRow) < view.layout.queueItems.count
        else {
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

        // Nothing is flipped, so a row's minY is its bottom edge and a menu placed there hangs
        // below it. AppKit flips the whole thing near an edge of the screen, which is the drawn
        // one's other failing.
        let anchor = view.layout.queueItems[Int(frame.menuRow)]
        if !menu.popUp(positioning: nil, at: CGPoint(x: anchor.minX, y: anchor.minY), in: view) {
            // Escape, a click elsewhere, or focus lost. The click that did it was swallowed by the
            // tracking loop, so this is the only way the managed side can hear about it.
            input.menuClosed = 1
        }
    }

    /// Drains what is queued and then blocks until the deadline, which is both the pump and the
    /// frame throttle. Without the second part this would spin a core, since the managed loop
    /// calls straight back in.
    private func pump() {
        let deadline = Date(timeIntervalSinceNow: 1.0 / 60.0)
        while let event = NSApp.nextEvent(matching: .any, until: deadline, inMode: .default, dequeue: true) {
            NSApp.sendEvent(event)
        }
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

    /// Builds the window if this runtime started headless, so a hidden start is still only a
    /// deferral rather than a different contract from the other heads. Only reachable from the
    /// managed loop's thread, which is the main one.
    func show() {
        makeWindow()
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func hide() {
        window?.orderOut(nil)
    }

    func shutdown() {
        window?.delegate = nil
        window?.orderOut(nil)
        window?.close()
        window = nil
        view = nil
        renderer = nil
        delegate = nil
        scroller = nil
        menuShown = false
        initialised = false
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
    }
}
