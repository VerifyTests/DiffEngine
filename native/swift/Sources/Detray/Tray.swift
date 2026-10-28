import AppKit
import PlatformHeadsMac

/// The status item, and what has come of it since the managed side last asked: the items chosen,
/// in the order they were, and whether AppKit has been asked to quit.
final class Tray {
    static let shared = Tray()

    let item = StatusItem()
    private let napGuard = NapGuard()
    private var chosen: [String] = []
    private(set) var quitting = false

    /// A logout, for an app with no Dock icon to quit from, turned into the managed side's own
    /// quit, which stages the queue on its way out. Held here: AppKit keeps its delegate weakly.
    private lazy var terminateGuard = TerminateGuard { [unowned self] in
        self.quitting = true
    }

    private init() {
        item.chose = { [unowned self] entry in
            self.chosen.append(entry.id)
        }
    }

    /// An accessory, with no Dock icon and no menu bar of its own, which a tray has no use for:
    /// not `HeadWindow.launch`, which makes a regular app so its window can take focus.
    /// `finishLaunching` is the part of `run` that has to happen before events are pumped by hand.
    /// App Nap is kept off, as the viewer keeps it: napped, a process's timers are coalesced, and
    /// the managed side's sockets and scan beside this answered late.
    func start() -> Bool {
        let application = NSApplication.shared
        application.setActivationPolicy(.accessory)
        application.delegate = terminateGuard
        application.finishLaunching()
        napGuard.begin(reason: "Tracking what test runs leave pending")
        return item.start()
    }

    /// A tenth of a second at most, which is what the managed loop turns on, and less when an
    /// item has been chosen or a quit asked for.
    func pump() {
        EventPump.pump(unseen: true) { self.hasInput }
    }

    private var hasInput: Bool {
        !chosen.isEmpty || quitting
    }

    func takeChosen() -> String? {
        chosen.isEmpty ? nil : chosen.removeFirst()
    }

    func stop() {
        napGuard.end()
    }
}
