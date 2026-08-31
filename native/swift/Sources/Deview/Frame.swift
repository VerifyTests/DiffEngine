import CDeview
import Foundation

/// One frame, decoded out of the flat description the managed side hands over.
///
/// Copied rather than read in place, because the pointers in `DeviewScreen` are only valid for the
/// duration of the call that carried them, and the view redraws whenever AppKit says so.
struct Frame {
    var title = ""
    var subtitle = ""
    var status = ""
    var queue: [QueueItem] = []

    /// Everything pending, which `queue` is not: that is the visible slice, sized to the body and
    /// with the members of folded groups left out.
    var pendingCount: Int32 = 0

    var buttons: [Button] = []
    var left = Pane()
    var right = Pane()

    /// The open context menu's labels, empty on almost every frame, and the queue row it hangs
    /// under.
    var menu: [String] = []
    var menuRow: Int32 = -1

    struct Row {
        var kind: Int32 = 0
        var lineNumber: Int32 = -1
        var text = ""

        /// What of `text` the reader has selected, in characters. Length 0 on a row with nothing
        /// selected, which is every row of almost every frame. The managed side has already
        /// resolved which side the drag is in and clipped the range to the visible slice, so this
        /// is a rectangle to fill rather than a range to work out.
        var selectStart: Int32 = 0
        var selectLength: Int32 = 0
    }

    struct Pane {
        var header = ""
        var rows: [Row] = []

        /// Where `rows` sits in the whole document, which is what the scroller needs and the rows
        /// themselves cannot say.
        var scrollTop: Int32 = 0
        var totalRows: Int32 = 0

        /// The picture this side is, drawn under the rows. Empty for a text side, and for an image
        /// side whose bytes could not be read or recognized — the managed side has already decided,
        /// so the renderer never has to. The size is the file header's, not a decoder's.
        var imagePath = ""
        var imageWidth: Int32 = 0
        var imageHeight: Int32 = 0
    }

    struct QueueItem {
        var label = ""
        var selected = false
        var failed = false
        var header = false

        /// What the row cannot say for itself, already composed, empty when there is nothing.
        var tooltip = ""
    }

    struct Button {
        var label = ""
        var enabled = false
    }

    static func decode(_ pointer: UnsafePointer<DeviewScreen>) -> Frame {
        let screen = pointer.pointee
        var frame = Frame()
        frame.title = string(screen, screen.titleOffset, screen.titleLength)
        frame.subtitle = string(screen, screen.subtitleOffset, screen.subtitleLength)
        frame.status = string(screen, screen.statusOffset, screen.statusLength)
        frame.pendingCount = screen.pendingCount

        if let items = screen.queue {
            for index in 0 ..< Int(screen.queueCount) {
                let item = items[index]
                frame.queue.append(
                    QueueItem(
                        label: string(screen, item.labelOffset, item.labelLength),
                        selected: item.flags & DEVIEW_QUEUE_SELECTED.value != 0,
                        failed: item.flags & DEVIEW_QUEUE_FAILED.value != 0,
                        header: item.flags & DEVIEW_QUEUE_HEADER.value != 0,
                        tooltip: string(screen, item.tooltipOffset, item.tooltipLength)))
            }
        }

        if let items = screen.menu, screen.menuCount > 0 {
            frame.menuRow = screen.menuRow
            for index in 0 ..< Int(screen.menuCount) {
                let item = items[index]
                frame.menu.append(string(screen, item.labelOffset, item.labelLength))
            }
        }

        if let buttons = screen.buttons {
            for index in 0 ..< Int(screen.buttonCount) {
                let button = buttons[index]
                frame.buttons.append(
                    Button(
                        label: string(screen, button.labelOffset, button.labelLength),
                        enabled: button.flags & DEVIEW_BUTTON_ENABLED.value != 0))
            }
        }

        if let panes = screen.panes, screen.paneCount >= 2 {
            frame.left = pane(screen, panes[0])
            frame.right = pane(screen, panes[1])
        }

        return frame
    }

    private static func pane(_ screen: DeviewScreen, _ source: DeviewPane) -> Pane {
        var pane = Pane()
        pane.header = string(screen, source.headerOffset, source.headerLength)
        pane.scrollTop = source.scrollTop
        pane.totalRows = source.totalRows
        pane.imagePath = string(screen, source.imagePathOffset, source.imagePathLength)
        pane.imageWidth = source.imageWidth
        pane.imageHeight = source.imageHeight
        guard let rows = screen.rows else {
            return pane
        }

        for index in 0 ..< Int(source.rowCount) {
            let offset = Int(source.rowOffset) + index
            guard offset >= 0, offset < Int(screen.rowCount) else {
                continue
            }

            let row = rows[offset]
            pane.rows.append(
                Row(
                    kind: row.kind,
                    lineNumber: row.lineNumber,
                    text: string(screen, row.textOffset, row.textLength),
                    selectStart: row.selectStart,
                    selectLength: row.selectLength))
        }

        return pane
    }

    /// Every offset is validated against the blob rather than trusted. This is the boundary the
    /// managed side reaches across, and a bad length here would be a read past the end of it.
    private static func string(_ screen: DeviewScreen, _ offset: Int32, _ length: Int32) -> String {
        guard length > 0,
              offset >= 0,
              let base = screen.strings,
              Int(offset) + Int(length) <= Int(screen.stringsLength)
        else {
            return ""
        }

        let buffer = UnsafeBufferPointer(start: base + Int(offset), count: Int(length))
        return String(decoding: buffer, as: UTF8.self)
    }
}
