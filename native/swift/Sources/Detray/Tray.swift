import AppKit
import CDetray
import PlatformHeadsMac

/// The status item, and what has come of it since the managed side last asked: the items chosen,
/// in the order they were, and whether AppKit has been asked to quit.
final class Tray {
    static let shared = Tray()

    let item = StatusItem()
    private let napGuard = NapGuard()
    private var chosen: [String] = []
    private(set) var quitting = false

    /// What `detray_on_session_end` was handed: the managed side's session end, which stages the
    /// queue it owns and removes the version marker.
    var sessionEnded: DetraySessionEnd?

    /// A quit sent to the app, which has no Dock icon to quit from, turned into the managed side's
    /// own quit, which stages the queue on its way out. A logout, restart or shutdown runs
    /// `sessionEnded` there and then instead, and AppKit ends the process once it returns. Made in
    /// `start`, from whatever `sessionEnded` is by then: with none, a session's end is refused as a
    /// quit is, which cancels the logout but loses nothing. Held here: AppKit keeps its delegate
    /// weakly.
    private var terminateGuard: TerminateGuard?

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
        let terminate = TerminateGuard(
            { [unowned self] in
                self.quitting = true
            },
            sessionEnding: sessionEnded.map { ended -> () -> Void in { ended() } })
        terminateGuard = terminate
        application.delegate = terminate
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
