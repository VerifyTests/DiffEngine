import CDetray
import PlatformHeadsMac

/// The menu as `detray_apply` hands it over, one array with each item's parent by index, made into
/// the tree `StatusItem` builds its menu from by PlatformHeadsMac's `StatusMenuItem.tree`. The
/// `DETRAY_*` flags are its `StatusMenuFlags`, at the same values.
enum MenuItems {
    static func read(_ items: UnsafePointer<DetrayItem>?, _ count: Int32) -> [StatusMenuItem] {
        guard let items, count > 0 else {
            return []
        }

        return StatusMenuItem.tree(
            Array(UnsafeBufferPointer(start: items, count: Int(count))),
            parent: { $0.parent }) { entry, children in
            StatusMenuItem(
                id: text(entry.id),
                label: text(entry.label),
                icon: text(entry.icon),
                flags: StatusMenuFlags(rawValue: entry.flags),
                children: children)
        }
    }

    private static func text(_ pointer: UnsafePointer<CChar>?) -> String {
        pointer.map { String(cString: $0) } ?? ""
    }
}
