// AppKit for NSAttributedString.Key.font and .foregroundColor, which are declared there rather
// than in Foundation.
import AppKit
import CDeview
import CoreGraphics
import CoreText
import Foundation

/// Draws a `Frame` with Core Text. Used both for the window and for the offscreen capture, so the
/// baselines describe what a user sees rather than a second code path.
///
/// Nothing is flipped. Core Graphics puts the origin bottom left, and layout here is expressed top
/// down and converted once in `rect`, which avoids having to fight the text matrix.
final class Renderer {
    /// Queue column widths, counted in character cells rather than points so a scaled display gets
    /// a column that holds the same number of characters rather than a narrower one.
    private static let defaultQueueCells: CGFloat = 34
    private static let minQueueCells: CGFloat = 8

    /// What the drag leaves each of the two panes, so the splitter cannot be pushed far enough
    /// right to squeeze them out of existence.
    private static let minPaneCells: CGFloat = 12

    /// How far either side of the rule counts as grabbing it. The rule is a single point, which is
    /// not something a mouse can be asked to hit.
    private static let grab: CGFloat = 4

    private static let padding: CGFloat = 6
    private static let gap: CGFloat = 4

    /// Marker, space, four digit line number, two spaces. Matches AsciiRenderer's gutter, so a
    /// line lands in the same column in both.
    private static let gutterCells: CGFloat = 8

    private let font: CTFont
    private let ascent: CGFloat
    private let descent: CGFloat

    /// Moved by dragging the rule between the queue and the panes. Kept here rather than in the
    /// view because this is what lays the rule out, and the drag has to land where it was drawn.
    private var queueWidth: CGFloat = 0

    /// One character cell. Measured from the font that was actually loaded, which is what the ABI
    /// reports back so the managed side can slice a pane to rows that fit.
    let cell: CGSize

    /// Where the clickable things ended up, for the view's hit testing. Returned from `draw`
    /// rather than stored, so an offscreen capture cannot overwrite the window's copy.
    struct Layout {
        var buttons: [CGRect] = []
        var queueItems: [CGRect] = []
        var menuItems: [CGRect] = []

        /// The grab zone around the rule between the queue and the panes, empty when there is no
        /// queue to divide off.
        var splitter: CGRect = .zero
    }

    init(fontData: Data?, size: CGFloat) {
        font = Renderer.load(fontData, size)
        ascent = CTFontGetAscent(font)
        descent = CTFontGetDescent(font)

        var character: UniChar = 0x4D // 'M'
        var glyph = CGGlyph()
        var advance = CGSize.zero
        if CTFontGetGlyphsForCharacters(font, &character, &glyph, 1) {
            _ = CTFontGetAdvancesForGlyphs(font, .horizontal, &glyph, &advance, 1)
        }

        cell = CGSize(
            width: max(1, advance.width.rounded()),
            height: max(1, (ascent + descent + CTFontGetLeading(font)).rounded(.up)))
        queueWidth = cell.width * Renderer.defaultQueueCells
    }

    /// Clamped on every use rather than only when dragged, so shrinking the window narrows the
    /// column instead of leaving the panes with nothing.
    private func clamp(_ value: CGFloat, _ width: CGFloat) -> CGFloat {
        let low = cell.width * Renderer.minQueueCells
        let high = max(
            low,
            width - Renderer.padding * 2 - Renderer.gap - cell.width * Renderer.minPaneCells * 2)
        return min(max(value, low), high)
    }

    /// Puts the rule under the cursor. Called by the view while the splitter is being dragged.
    func dragQueueWidth(to x: CGFloat, in width: CGFloat) {
        queueWidth = clamp(x - Renderer.padding - Renderer.gap / 2, width)
    }

    private static func load(_ data: Data?, _ size: CGFloat) -> CTFont {
        guard let data,
              !data.isEmpty,
              let provider = CGDataProvider(data: data as CFData),
              let cgFont = CGFont(provider)
        else {
            // Nothing embedded, so take the system monospaced face.
            return CTFontCreateWithName("Menlo" as CFString, size, nil)
        }

        // Registered process wide so Core Text can resolve it by name later if it needs to. A
        // duplicate registration is not an error worth failing over, hence the ignored result.
        var error: Unmanaged<CFError>?
        _ = CTFontManagerRegisterGraphicsFont(cgFont, &error)
        error?.release()
        return CTFontCreateWithGraphicsFont(cgFont, size, nil, nil)
    }

    /// The window size in character cells, which is what version 2 of the ABI reports.
    func grid(for size: CGSize) -> (columns: Int32, rows: Int32) {
        (Int32(size.width / cell.width), Int32(size.height / cell.height))
    }

    @discardableResult
    func draw(_ frame: Frame, in context: CGContext, size: CGSize) -> Layout {
        var layout = Layout()
        context.setFillColor(Palette.background)
        context.fill(CGRect(origin: .zero, size: size))

        let line = cell.height
        let hasQueue = !frame.queue.isEmpty
        let queue = hasQueue ? clamp(queueWidth, size.width) : 0
        let panesLeft = hasQueue ? Renderer.padding + queue + Renderer.gap : Renderer.padding
        let panesWidth = max(cell.width * 2, size.width - Renderer.padding - panesLeft)
        let half = (panesWidth / 2).rounded(.down)

        text(frame.title, in: rect(top: Renderer.padding, left: Renderer.padding, width: size.width - Renderer.padding * 2, height: line, size), Palette.text, context)
        if !frame.subtitle.isEmpty {
            let width = CGFloat(frame.subtitle.count) * cell.width
            text(frame.subtitle, in: rect(top: Renderer.padding, left: size.width - Renderer.padding - width, width: width, height: line, size), Palette.dim, context)
        }

        let firstRule = Renderer.padding + line + Renderer.gap
        rule(top: firstRule, width: size.width, in: context, size)

        let headerTop = firstRule + Renderer.gap
        if hasQueue {
            // Entries only: the rows include group headings, which are not pending anything.
            let pending = frame.queue.filter { !$0.header }.count
            text("Pending (\(pending))", in: rect(top: headerTop, left: Renderer.padding, width: queue, height: line, size), Palette.text, context)
        }

        text(frame.left.header, in: rect(top: headerTop, left: panesLeft, width: half, height: line, size), Palette.text, context)
        text(frame.right.header, in: rect(top: headerTop, left: panesLeft + half, width: half, height: line, size), Palette.text, context)
        rule(top: headerTop + line + Renderer.gap, width: size.width, in: context, size)

        let bodyTop = Renderer.padding + (line + Renderer.gap) * 2 + Renderer.gap * 2
        let footerHeight = line + Renderer.gap * 2
        let capacity = max(1, Int((size.height - bodyTop - footerHeight - Renderer.padding) / line))
        let rows = min(capacity, max(frame.queue.count, max(frame.left.rows.count, frame.right.rows.count)))

        for index in 0 ..< rows {
            let top = bodyTop + CGFloat(index) * line
            if hasQueue {
                let bounds = rect(top: top, left: Renderer.padding, width: queue, height: line, size)
                layout.queueItems.append(bounds)
                queueItem(frame, index, bounds, context)
            }

            row(frame.left, index, rect(top: top, left: panesLeft, width: half, height: line, size), context)
            row(frame.right, index, rect(top: top, left: panesLeft + half, width: panesWidth - half, height: line, size), context)
        }

        let bodyBottom = bodyTop + CGFloat(capacity) * line
        if hasQueue {
            let ruleLeft = panesLeft - Renderer.gap / 2
            columnRule(left: ruleLeft, top: bodyTop, bottom: bodyBottom, in: context, size)
            layout.splitter = rect(
                top: bodyTop,
                left: ruleLeft - Renderer.grab,
                width: Renderer.grab * 2 + 1,
                height: bodyBottom - bodyTop,
                size)
        }

        columnRule(left: panesLeft + half - Renderer.gap / 2, top: bodyTop, bottom: bodyBottom, in: context, size)

        layout.buttons = footer(frame, size: size, height: footerHeight, line: line, in: context)
        menu(frame, into: &layout, in: context)
        return layout
    }

    /// The context menu, floated one row under its queue row, last so it draws over whatever it
    /// overlaps. The managed side owns opening and closing; this draws what the frame carries and
    /// the view reports which recorded rectangle was clicked.
    private func menu(_ frame: Frame, into layout: inout Layout, in context: CGContext) {
        guard !frame.menu.isEmpty,
              frame.menuRow >= 0,
              Int(frame.menuRow) < layout.queueItems.count
        else {
            return
        }

        let anchor = layout.queueItems[Int(frame.menuRow)]
        let widest = frame.menu.map { $0.count }.max() ?? 0
        let panelWidth = CGFloat(widest + 2) * cell.width
        let height = CGFloat(frame.menu.count) * cell.height + Renderer.padding * 2
        // Not flipped, so "one row below" is a lower y.
        let panel = CGRect(
            x: anchor.minX + cell.width,
            y: anchor.minY - height,
            width: panelWidth,
            height: height)
        context.setFillColor(Palette.filler)
        context.fill(panel)
        context.setStrokeColor(Palette.rule)
        context.stroke(panel, width: 1)

        for (index, label) in frame.menu.enumerated() {
            let item = CGRect(
                x: panel.minX,
                y: panel.maxY - Renderer.padding - CGFloat(index + 1) * cell.height,
                width: panel.width,
                height: cell.height)
            layout.menuItems.append(item)
            text(
                label,
                in: CGRect(
                    x: item.minX + cell.width,
                    y: item.minY,
                    width: item.width - cell.width,
                    height: item.height),
                Palette.text,
                context)
        }
    }

    private func footer(_ frame: Frame, size: CGSize, height: CGFloat, line: CGFloat, in context: CGContext) -> [CGRect] {
        let top = size.height - height - Renderer.padding
        rule(top: top - Renderer.gap, width: size.width, in: context, size)

        var rects: [CGRect] = []
        var left = Renderer.padding
        for button in frame.buttons {
            let width = CGFloat(button.label.count + 4) * cell.width
            let bounds = rect(top: top, left: left, width: width, height: height, size)
            rects.append(bounds)

            context.setFillColor(button.enabled ? Palette.buttonFace : Palette.buttonDisabled)
            context.fill(bounds)
            let label = bounds.insetBy(dx: cell.width * 2, dy: (height - line) / 2)
            text(button.label, in: label, button.enabled ? Palette.text : Palette.dim, context)
            left += width + Renderer.gap
        }

        if !frame.status.isEmpty {
            let width = CGFloat(frame.status.count) * cell.width
            let bounds = rect(top: top + (height - line) / 2, left: size.width - Renderer.padding - width, width: width, height: line, size)
            text(frame.status, in: bounds, Palette.dim, context)
        }

        return rects
    }

    private func queueItem(_ frame: Frame, _ index: Int, _ bounds: CGRect, _ context: CGContext) {
        guard index < frame.queue.count else {
            return
        }

        let item = frame.queue[index]
        if item.header {
            // A heading, not a row: dimmed like the subtitle, flush left, no selection fill.
            text(item.label, in: bounds, Palette.dim, context)
            return
        }

        if item.selected {
            context.setFillColor(Palette.selected)
            context.fill(bounds)
        }

        let label = item.failed ? "\(item.label) !" : item.label
        let colour = item.failed ? Palette.foreground(DEVIEW_ROW_REMOVED.value) : Palette.text
        // Indented rather than offset: offsetBy keeps the width, which would let a long name clip
        // one cell past the column instead of at it.
        text(
            label,
            in: CGRect(
                x: bounds.minX + cell.width,
                y: bounds.minY,
                width: bounds.width - cell.width,
                height: bounds.height),
            colour,
            context)
    }

    private func row(_ pane: Frame.Pane, _ index: Int, _ bounds: CGRect, _ context: CGContext) {
        guard index < pane.rows.count else {
            return
        }

        let row = pane.rows[index]
        if let background = Palette.rowBackground(row.kind) {
            context.setFillColor(background)
            context.fill(bounds)
        }

        if row.kind == DEVIEW_ROW_FILLER.value {
            return
        }

        let number = String(row.lineNumber)
        let gutter = "\(Palette.marker(row.kind)) \(String(repeating: " ", count: max(0, 4 - number.count)))\(number)"
        let width = Renderer.gutterCells * cell.width
        text(gutter, in: CGRect(x: bounds.minX, y: bounds.minY, width: width, height: bounds.height), Palette.dim, context)
        text(
            row.text,
            in: CGRect(x: bounds.minX + width, y: bounds.minY, width: bounds.width - width, height: bounds.height),
            Palette.foreground(row.kind),
            context)
    }

    /// Clipped to its own rect, so a long line stops at its column instead of running into the
    /// next one.
    private func text(_ string: String, in bounds: CGRect, _ colour: CGColor, _ context: CGContext) {
        guard !string.isEmpty, bounds.width > 0 else {
            return
        }

        let attributed = NSAttributedString(
            string: RowText.flatten(string),
            attributes: [
                .font: font,
                .foregroundColor: colour
            ])

        context.saveGState()
        context.clip(to: bounds)
        context.textPosition = CGPoint(x: bounds.minX, y: bounds.minY + descent)
        CTLineDraw(CTLineCreateWithAttributedString(attributed), context)
        context.restoreGState()
    }

    private func rule(top: CGFloat, width: CGFloat, in context: CGContext, _ size: CGSize) {
        context.setFillColor(Palette.rule)
        context.fill(rect(top: top, left: Renderer.padding, width: width - Renderer.padding * 2, height: 1, size))
    }

    private func columnRule(left: CGFloat, top: CGFloat, bottom: CGFloat, in context: CGContext, _ size: CGSize) {
        context.setFillColor(Palette.rule)
        context.fill(rect(top: top, left: left, width: 1, height: bottom - top, size))
    }

    /// Top down layout into Core Graphics' bottom left origin, in one place.
    private func rect(top: CGFloat, left: CGFloat, width: CGFloat, height: CGFloat, _ size: CGSize) -> CGRect {
        CGRect(x: left, y: size.height - top - height, width: max(0, width), height: height)
    }
}

/// A tab or a stray newline would break a character grid. Every renderer has to resolve them the
/// same way or the text snapshots stop describing what the pixel ones show, so this matches
/// RowText.Flatten on the managed side.
enum RowText {
    static func flatten(_ text: String) -> String {
        guard text.contains(where: { $0 == "\t" || $0 == "\r" || $0 == "\n" }) else {
            return text
        }

        return text
            .replacingOccurrences(of: "\t", with: "    ")
            .replacingOccurrences(of: "\r", with: "")
            .replacingOccurrences(of: "\n", with: " ")
    }
}
