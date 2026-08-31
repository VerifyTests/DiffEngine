import AppKit
import CDeview

/// What AppKit's own controls send to: the menu bar, the context menu and the scroller.
///
/// One object rather than three, because all any of them does is record an event for the next
/// `deview_poll_input` to drain. Nothing here decides anything; the managed side does.
final class ControlTarget: NSObject {
    /// Deliberately not `NSApplication.terminate`, which would end the process out from under the
    /// managed loop. Closing is the managed side's decision — with a tray to reopen from the
    /// viewer hides rather than exits — so Quit says exactly what the `q` key says and lets the
    /// loop answer it.
    @objc
    func quit(_ sender: Any?) {
        Runtime.shared.input.key = DEVIEW_KEY_QUIT.value
    }

    /// A menu bar command. The item's tag is the `DeviewKey`, so the menu carries no mapping of
    /// its own to drift from `ViewerView.map`.
    @objc
    func command(_ sender: Any?) {
        guard let item = sender as? NSMenuItem else {
            return
        }

        Runtime.shared.input.key = Int32(item.tag)
    }

    /// A context menu item. The tag is its index in the frame's menu, which is the whole payload:
    /// the managed side resolves it against the menu it built.
    @objc
    func contextItem(_ sender: Any?) {
        guard let item = sender as? NSMenuItem else {
            return
        }

        Runtime.shared.input.clickedMenuItem = Int32(item.tag)
    }

    @objc
    func scrolled(_ sender: Any?) {
        guard let scroller = sender as? NSScroller else {
            return
        }

        Runtime.shared.scrolled(scroller)
    }
}

/// The menu bar.
///
/// Built by hand because this head is a bare executable rather than an app bundle, so there is no
/// nib to load one from. Without it the app has a Dock icon, takes focus, and has no Quit, no
/// Close and no Minimise, which on macOS reads as broken rather than as minimal.
enum MainMenu {
    static func build(_ target: ControlTarget) -> NSMenu {
        let name = ProcessInfo.processInfo.processName
        let bar = NSMenu()
        bar.addItem(submenu(NSMenu(title: name), items: application(target, name)))
        bar.addItem(submenu(NSMenu(title: "Edit"), items: edit(target)))
        bar.addItem(submenu(NSMenu(title: "Snapshot"), items: snapshot(target)))
        bar.addItem(submenu(NSMenu(title: "Window"), items: window()))
        return bar
    }

    private static func application(_ target: ControlTarget, _ name: String) -> [NSMenuItem] {
        let hideOthers = item(
            "Hide Others",
            #selector(NSApplication.hideOtherApplications(_:)),
            key: "h")
        hideOthers.keyEquivalentModifierMask = [.command, .option]

        return [
            item("Hide \(name)", #selector(NSApplication.hide(_:)), key: "h"),
            hideOthers,
            item("Show All", #selector(NSApplication.unhideAllApplications(_:))),
            .separator(),
            item("Quit \(name)", #selector(ControlTarget.quit(_:)), key: "q", target: target)
        ]
    }

    /// The two commands that do carry key equivalents, because both are chords rather than plain
    /// letters and so cannot swallow the keystrokes `ViewerView.keyDown` exists to read. They are
    /// also the two a macOS reader will try before reading any documentation.
    private static func edit(_ target: ControlTarget) -> [NSMenuItem] {
        let copy = command("Copy", DEVIEW_KEY_COPY, target)
        copy.keyEquivalent = "c"
        let selectAll = command("Select All", DEVIEW_KEY_SELECT_ALL, target)
        selectAll.keyEquivalent = "a"
        return [copy, selectAll]
    }

    /// The keymap the docs publish, one item per command, so it is discoverable rather than only
    /// documented.
    ///
    /// None of these carries a key equivalent. The keys are plain letters, and a modifier-less
    /// equivalent is matched before the key ever reaches the view, so the menu would swallow every
    /// keystroke `ViewerView.keyDown` exists to read. The key goes in the title instead.
    private static func snapshot(_ target: ControlTarget) -> [NSMenuItem] {
        [
            command("Accept (a)", DEVIEW_KEY_ACCEPT, target),
            command("Accept All (⇧A)", DEVIEW_KEY_ACCEPT_ALL, target),
            command("Discard (d)", DEVIEW_KEY_DISCARD, target),
            command("Next Variant (v)", DEVIEW_KEY_NEXT_VARIANT, target),
            .separator(),
            command("Next Change (n)", DEVIEW_KEY_NEXT_CHANGE, target),
            command("Previous Change (p)", DEVIEW_KEY_PREVIOUS_CHANGE, target),
            .separator(),
            command("Next Pending (⇥)", DEVIEW_KEY_NEXT_ITEM, target),
            command("Previous Pending (⇧⇥)", DEVIEW_KEY_PREVIOUS_ITEM, target)
        ]
    }

    /// `performClose` rather than anything of ours: it routes through `windowShouldClose`, which is
    /// already where hide versus exit is decided.
    private static func window() -> [NSMenuItem] {
        [
            item("Minimize", #selector(NSWindow.performMiniaturize(_:)), key: "m"),
            item("Close", #selector(NSWindow.performClose(_:)), key: "w")
        ]
    }

    private static func command(_ title: String, _ key: DeviewKey, _ target: ControlTarget) -> NSMenuItem {
        let entry = item(title, #selector(ControlTarget.command(_:)), target: target)
        entry.tag = Int(key.value)
        return entry
    }

    /// A nil target is not an omission: it sends the selector down the responder chain, which is
    /// how the AppKit supplied actions above reach the application and the key window, and is also
    /// what enables and disables them.
    private static func item(
        _ title: String,
        _ action: Selector,
        key: String = "",
        target: AnyObject? = nil) -> NSMenuItem {
        let entry = NSMenuItem(title: title, action: action, keyEquivalent: key)
        entry.target = target
        return entry
    }

    private static func submenu(_ menu: NSMenu, items: [NSMenuItem]) -> NSMenuItem {
        for entry in items {
            menu.addItem(entry)
        }

        let holder = NSMenuItem()
        holder.title = menu.title
        holder.submenu = menu
        return holder
    }
}
