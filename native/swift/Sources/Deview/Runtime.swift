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

        let application = NSApplication.shared
        // Regular rather than accessory, so the window can take focus and appear in the dock
        // without this being an app bundle. finishLaunching is the part of run() that has to
        // happen before events are pumped by hand.
        application.setActivationPolicy(.regular)
        application.finishLaunching()

        let renderer = Renderer(fontData: font, size: fontSize)
        let bounds = NSRect(x: 0, y: 0, width: CGFloat(width), height: CGFloat(height))
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

        self.renderer = renderer
        self.view = view
        self.window = window
        self.delegate = delegate
        initialised = true

        if !hidden {
            show()
        }

        measureGrid()
        return true
    }

    func present(_ frame: Frame) {
        view?.model = frame
        view?.needsDisplay = true
        view?.displayIfNeeded()
        pump()
        measureGrid()
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
        guard let renderer, let view else {
            return
        }

        let grid = renderer.grid(for: view.bounds.size)
        input.columns = grid.columns
        input.rows = grid.rows
    }

    func show() {
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
        initialised = false
    }

    /// Each event is delivered exactly once, so the poll that read them clears them. The grid is
    /// left alone: it is a state, not an event.
    func resetInput() {
        input.key = DEVIEW_KEY_NONE.value
        input.clickedButton = -1
        input.clickedQueueItem = -1
        input.scrollDelta = 0
        input.closeRequested = 0
    }
}
