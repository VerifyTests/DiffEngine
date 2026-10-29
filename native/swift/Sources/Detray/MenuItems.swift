import CDetray
import PlatformHeadsMac

/// The menu as `detray_apply` hands it over, one array with each item's parent by index, made into
/// the tree `StatusItem` builds its menu from; and that tree as text, for `detray_describe`.
enum MenuItems {
    static func read(_ items: UnsafePointer<DetrayItem>?, _ count: Int32) -> [StatusMenuItem] {
        guard let items, count > 0 else {
            return []
        }

        return children(of: -1, in: UnsafeBufferPointer(start: items, count: Int(count)))
    }

    private static func children(of parent: Int32, in flat: UnsafeBufferPointer<DetrayItem>) -> [StatusMenuItem] {
        var result: [StatusMenuItem] = []
        for (index, entry) in flat.enumerated() where entry.parent == parent {
            let checkable = entry.flags & DETRAY_CHECKABLE != 0
            result.append(
                StatusMenuItem(
                    id: text(entry.id),
                    label: text(entry.label),
                    icon: text(entry.icon),
                    enabled: entry.flags & DETRAY_ENABLED != 0,
                    separator: entry.flags & DETRAY_SEPARATOR != 0,
                    checked: checkable ? entry.flags & DETRAY_CHECKED != 0 : nil,
                    children: children(of: Int32(index), in: flat)))
        }

        return result
    }

    private static func text(_ pointer: UnsafePointer<CChar>?) -> String {
        pointer.map { String(cString: $0) } ?? ""
    }

    /// A line an item, two spaces deeper for each submenu: its label and id, and whatever else it
    /// has, the way the managed tests write a menu.
    static func describe(_ items: [StatusMenuItem], depth: Int = 0) -> String {
        var lines = ""
        for item in items {
            lines += String(repeating: " ", count: depth * 2)
            if item.separator {
                lines += "----\n"
                continue
            }

            lines += "\(item.label)  [id: \(item.id)]"
            if !item.icon.isEmpty {
                lines += "  [glyph: \(item.icon)]"
            }

            if !item.enabled {
                lines += "  [disabled]"
            }

            if let checked = item.checked {
                lines += checked ? "  [checked]" : "  [unchecked]"
            }

            lines += "\n"
            lines += describe(item.children, depth: depth + 1)
        }

        return lines
    }
}
