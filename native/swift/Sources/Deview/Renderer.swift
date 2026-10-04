// AppKit for NSAttributedString.Key.font and .foregroundColor, which are declared there rather
// than in Foundation.
import AppKit
import CDeview
import CoreGraphics
import CoreText
import Foundation
import ImageIO

/// Draws a `Frame` with Core Text. Used both for the window and for the offscreen capture, so the
/// baselines describe what a user sees rather than a second code path.
///
/// What is drawn here is the grid and its chrome. The context menu, the tooltips and the scroller
/// are AppKit's, because a hand drawn menu has no keyboard, no Escape and nothing for VoiceOver to
/// read. The cost is that none of the three exists in a capture, which never makes a window: the
/// scroller answers for that by taking no width without one, and the menu by no longer having a
/// baseline at all on this platform.
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

    /// The side of a checker square behind a picture, so an image with transparency reads as
    /// transparent rather than as whatever colour the pane happens to be.
    private static let checkerSize: CGFloat = 8

    /// Marker, space, four digit line number, two spaces. Matches AsciiRenderer's gutter, so a
    /// line lands in the same column in both.
    private static let gutterCells: CGFloat = 8

    /// The lines the managed side keeps for everything that is not a row of the body, which it
    /// takes off the rows it is told the window has. Keep in sync with ScreenBuilder.Chrome.
    private static let chromeRows = 8

    /// How many rows the body had room for in the window's last draw, above whatever footer that
    /// draw laid out, and the size the window was then. Nil before there has been one: see `grid`.
    private var room: (size: CGSize, rows: Int)?

    private let font: CTFont
    private let ascent: CGFloat
    private let descent: CGFloat

    /// Moved by dragging the rule between the queue and the panes. Kept here rather than in the
    /// view because this is what lays the rule out, and the drag has to land where it was drawn.
    private var queueWidth: CGFloat = 0

    /// What the draw in progress can reach, or nil for everything: the bounds of the context's
    /// clip, outside which nothing is painted whatever is asked for. What lies wholly outside it is
    /// not laid out either: see `shows`.
    ///
    /// A spinner turns by invalidating its own rectangle, some twenty times a second for as long
    /// as a page takes. Where AppKit narrows the clip to that rectangle, a turn lays out the
    /// spinner and nothing else, where it used to lay out every line of text in the window to
    /// paint none of it.
    ///
    /// It does not always narrow it, which is why the clip is read at each draw and never worked
    /// out from what was invalidated. Since macOS 11 a view whose backing store AppKit manages can
    /// be handed its whole bounds whatever was invalidated, clip included, and later versions ask
    /// for the whole of a view when they see fit. Then all of it is drawn, as it has to be, and
    /// what keeps that cheap is `lines`.
    ///
    /// Set as a draw begins and read only during it.
    private var dirty: CGRect?

    /// Both panes name the one picture in the draw in progress, as a byte equal pair of documents
    /// does: their pages are kept under the hash of the document. The two panes can be a point
    /// apart in width, and so can ask for copies of different sizes. With one copy a picture each
    /// had it made for its own size in turn, without end: every one that landed was a redraw, in
    /// which the other pane found the copy was not its own and asked again. So a picture both
    /// panes name keeps the last two copies made, which is one for each: see `Picture.spare`.
    /// An enlarged pair makes none: it is drawn from the picture, as every enlarged picture was
    /// before it had a copy.
    ///
    /// As the last draw to begin left it. `settle` reads it for a copy that has landed, which
    /// was asked for by that draw or by one before it.
    private var shared = false

    /// Decoded pictures, keyed by the path the screen model handed over and invalidated by the
    /// file's write time and length — the same freshness test the queue poller uses, so a re-run
    /// that rewrites a received image refreshes the pane rather than leaving the previous one up.
    ///
    /// A nil `image` once `decoding` is over is a remembered failure, so something ImageIO cannot
    /// read is attempted once rather than on every redraw, and AppKit redraws for a great many
    /// reasons.
    ///
    /// Decoded and scaled on `work` rather than here, for the window. A page of a document or a
    /// large screenshot takes tens of milliseconds and more to decode and as long again to scale,
    /// and done on the main thread the window answered nothing until both were. A capture still
    /// does both there and then: it draws one frame, and has no later one for them to land in.
    private var pictures: [String: Picture] = [:]

    private struct Picture {
        var image: CGImage?
        var modified: Date
        var length: UInt64

        /// The picture scaled down to the device pixels it was last drawn at: the ones it fills
        /// when it is fitted, and the ones the whole of it takes, shown or not, when it is enlarged
        /// and still below its own size. Drawing a large picture scaled costs a resample of every
        /// source pixel, and a window showing one did that on every redraw; this is a copy.
        ///
        /// One copy, for whichever size was asked for last. It is only made narrower than the
        /// picture or shorter, so at its largest it is about the size the picture is itself. It
        /// goes when a copy for another size lands, and with the picture.
        var scaled: CGImage?

        /// The copy `scaled` took the place of, kept only while both panes name this picture: see
        /// `shared`. Each pane then has the copy made for its own size, one here and one there.
        var spare: CGImage?

        /// Being decoded on `work`. The pane shows a spinner until it lands.
        var decoding = false

        /// The size being scaled to on `work`, or nil when nothing is. One at a time, so a window
        /// being resized is scaled for the size it has when the last one lands, rather than for
        /// every size it passed through on the way.
        var scaling: Pixels?

        /// Scaling it failed, so it is drawn as it is, scaled as it is drawn, rather than asked for
        /// again on every redraw.
        var unscalable = false
    }

    /// The pictures the last draw had no room for, each with its file as it was then.
    ///
    /// Nothing is decoded for one, so it is not in `pictures`, and `picturesChanged` took a picture
    /// that is not there for one that has just appeared: a window too short for its picture, about
    /// 176 points, was redrawn sixty times a second to draw none of it. Kept apart from `pictures`
    /// because an entry there with no image is one ImageIO could not read, which is never tried
    /// again, and this one is to be decoded as soon as there is room.
    private var unplaced: [String: Stamp] = [:]

    /// A file as it was when it was last looked at, which is how a rewritten one is told.
    private struct Stamp: Equatable {
        var modified: Date
        var length: UInt64
    }

    /// A size in device pixels, which is what a scaled copy is made at and matched by.
    private struct Pixels: Equatable {
        var width: Int
        var height: Int
    }

    private let work = DispatchQueue(label: "DiffEngineViewer.pictures", qos: .userInitiated)

    /// What `work` has finished, for `takeFinished` to put in place, and the paths the last frame
    /// drew, which `work` reads to skip a job for a picture that has left the screen since it was
    /// asked for. Both behind `gate`, being the only things here two threads touch.
    private let gate = NSLock()
    private var finished: [Finished] = []
    private var wanted: Set<String> = []

    /// Something landed during a draw that painted only part of the window, or none of it, and
    /// the whole of it has not been drawn since: see `draw` and `takeFinished`.
    private var owed = false

    private enum Finished {
        case decoded(path: String, modified: Date, length: UInt64, image: CGImage?)
        case scaled(path: String, modified: Date, length: UInt64, size: Pixels, image: CGImage?)
    }

    /// The lines the last two draws drew, by what each was made from.
    ///
    /// Every piece of text is an attributed string and a line, and each was made again on every
    /// draw: about a hundred and fifty for a window of plain text, and one more for every
    /// character the managed side sends as a segment of its own, which is most of a pane of
    /// Chinese. Yet most draws draw what the one before drew. A turn of a spinner or a dragged
    /// picture changes none of the text, and a scroll keeps all but a row of it. So a line is
    /// kept for as long as it goes on being drawn.
    ///
    /// `lines` is what this draw has drawn so far, and `earlier` what the last draw to draw any
    /// text drew. A line found in `earlier` is carried into `lines`, and what is still only in
    /// `earlier` when `lines` takes its place was not drawn again and goes with it. That is the
    /// bound: two draws' worth of lines, however long the session.
    ///
    /// A line is made from its text, its colour and the font. The first two are the key, and the
    /// font is this renderer's for as long as it lives, so a line kept here is never one that
    /// would be made differently now. A capture draws from here too: it is the same line.
    private var lines: [LineKey: CTLine] = [:]
    private var earlier: [LineKey: CTLine] = [:]

    /// What a kept line was made from. Two texts are the same when their bytes are, rather than
    /// as Swift compares strings, which holds a composed character equal to the same one
    /// decomposed: Core Text is handed different characters for the two, and need not draw them
    /// alike.
    ///
    /// The colour by which object it is, which `Palette` makes the same as which colour it is by
    /// handing out one object each. Asking Core Foundation whether two are equal instead would
    /// rest on Core Graphics hashing equal colours alike, which nothing here can check, and a key
    /// whose hash and equality disagree is one a dictionary can trap on. The colour is held as
    /// well as compared, so nothing else can be at its address while a line is kept under it.
    private struct LineKey: Hashable {
        let text: String
        let colour: CGColor

        static func == (left: LineKey, right: LineKey) -> Bool {
            left.colour === right.colour && left.text.utf8.elementsEqual(right.text.utf8)
        }

        func hash(into hasher: inout Hasher) {
            hasher.combine(text)
            hasher.combine(ObjectIdentifier(colour))
        }
    }

    /// One character cell. Measured from the font that was actually loaded, which is what the ABI
    /// reports back so the managed side can slice a pane to rows that fit.
    let cell: CGSize

    /// How much of the right edge belongs to something other than this renderer. Set by `Runtime`
    /// only when there is a window and the platform draws scrollers that take space; a capture has
    /// neither, so it stays zero and the baselines are unaffected by the scroller existing.
    var rightInset: CGFloat = 0

    /// Where the clickable things ended up, for the view's hit testing. Returned from `draw`
    /// rather than stored, so an offscreen capture cannot overwrite the window's copy.
    struct Layout {
        var buttons: [CGRect] = []
        var queueItems: [CGRect] = []

        /// The grab zone around the rule between the queue and the panes, empty when there is no
        /// queue to divide off.
        var splitter: CGRect = .zero

        /// The rows region, which is what a scroller spans and what the tooltips sit inside.
        var body: CGRect = .zero

        /// Where the two panes' columns ended up, left then right, so a drag selecting text is
        /// resolved against the same numbers that drew it.
        var panes: [PaneColumn] = []

        /// Where a spinner was drawn, standing in for a picture on its way. What `Runtime` redraws
        /// to turn it, rather than the whole window.
        var spinners: [CGRect] = []

        /// Where each pane's picture goes, or the spinner standing in for one: what the wheel and
        /// a press are resolved against.
        var pictures: [PictureSpace] = []
    }

    /// The space under a pane's rows that its picture is drawn in. The whole space rather than the
    /// picture, and whether or not the picture has arrived: a small picture is a small target, and
    /// a wheel turned beside it means the same thing.
    struct PictureSpace: Equatable {
        var bounds: CGRect = .zero

        /// Enlarged past the space, so there is somewhere for a drag to take it: the whole
        /// picture's size as drawn, the centre it was drawn about as fractions of it, with y
        /// counted from its top, and how much of it shows each way.
        var enlarged = false
        var whole: CGSize = .zero
        var centre: CGPoint = CGPoint(x: 0.5, y: 0.5)
        var across: CGFloat = 1
        var down: CGFloat = 1

        /// The centre the frame asked for, before it was moved in to keep this space full, and
        /// whether there is more of the picture than the space shows each way, by a whole point
        /// or more: which is whether a drag can move it that way.
        var asked: CGPoint = CGPoint(x: 0.5, y: 0.5)
        var movesAcross = false
        var movesDown = false

        /// Where a drag of `by` points leaves the centre. The picture follows the pointer, so the
        /// point at the middle moves the other way, and nothing here is flipped, so a drag up the
        /// screen is a positive y and brings what is lower in the picture into view.
        ///
        /// The centre is one point for both panes, and the other pane's picture need not be this
        /// one's shape. So a way this picture cannot move is reported as the frame had it, not as
        /// this space holds it: all of it shows that way, which held here is the middle, and
        /// reporting the middle put the other pane's picture back there on the first move of a
        /// drag that was along the other axis.
        ///
        /// And a way both can move, it goes as far as the one that can go further. What is moved
        /// is the centre the frame asked for, not the one this space drew about, and it is kept
        /// inside what the pane showing less of its picture can show: `other` is that pane's
        /// space, or nil when it has no picture. Moved from this space's own and kept to this
        /// space's range, the first move of a drag brought the other pane's picture in from
        /// wherever beyond that range it had been dragged to.
        func dragged(by: CGSize, other: PictureSpace?) -> CGPoint {
            guard enlarged, whole.width > 0, whole.height > 0 else {
                return centre
            }

            var acrossShown = across
            var downShown = down
            if let other, other.enlarged {
                if other.movesAcross {
                    acrossShown = min(acrossShown, other.across)
                }

                if other.movesDown {
                    downShown = min(downShown, other.down)
                }
            }

            let x = PictureSpace.kept(PictureSpace.kept(asked.x, acrossShown) - by.width / whole.width, acrossShown)
            let y = PictureSpace.kept(PictureSpace.kept(asked.y, downShown) + by.height / whole.height, downShown)
            return CGPoint(
                x: movesAcross ? x : asked.x,
                y: movesDown ? y : asked.y)
        }

        /// A centre moved in as far as it takes for a space showing so much of its picture to
        /// stay full.
        private static func kept(_ centre: CGFloat, _ shown: CGFloat) -> CGFloat {
            min(max(centre, shown / 2), 1 - shown / 2)
        }
    }

    /// One pane's horizontal extent: the column, and where its row text starts past the gutter.
    struct PaneColumn {
        var cellLeft: CGFloat = 0
        var textLeft: CGFloat = 0
        var width: CGFloat = 0
    }

    init(fontData: Data?, size: CGFloat) {
        font = Renderer.withoutLigatures(Renderer.load(fontData, size), size)
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

    /// `font` with its ligatures off, so every character of a snapshot is drawn as itself.
    ///
    /// JetBrains Mono draws `<>`, `!=`, `<=`, `=>`, `->`, `==` and a good many more as one glyph
    /// each, and closes up `...`, all through its `calt` feature, which Core Text applies unless
    /// told not to. The other two heads draw a glyph a character, so the same title read `<>` on
    /// Windows and Linux and as one diamond here, in a tool whose whole job is to show which
    /// characters a snapshot holds.
    ///
    /// `liga` goes off with it. The embedded font has no such feature, but the face taken when
    /// nothing is embedded might, and the answer should not turn on which font is in use.
    private static func withoutLigatures(_ font: CTFont, _ size: CGFloat) -> CTFont {
        let features: [[CFString: Any]] = [
            [kCTFontOpenTypeFeatureTag: "calt", kCTFontOpenTypeFeatureValue: 0],
            [kCTFontOpenTypeFeatureTag: "liga", kCTFontOpenTypeFeatureValue: 0]
        ]
        let attributes: [CFString: Any] = [kCTFontFeatureSettingsAttribute: features]
        let descriptor = CTFontDescriptorCreateWithAttributes(attributes as CFDictionary)
        return CTFontCreateCopyWithAttributes(font, size, nil, descriptor)
    }

    /// The window size in character cells, which is what version 2 of the ABI reports. Net of the
    /// scroller, because a column the scroller is sitting on is not a column the diff can use.
    ///
    /// And no more rows than the body has room for, with the lines the managed side takes off for
    /// everything else added back. That side keeps eight lines, which is 64 points more than this
    /// head's title, headers and a footer of one row take, so a footer of three rows of buttons,
    /// or two and a status line, fits in what is over and the window's height in rows is the
    /// answer, as it always was. A taller one does not, and the body ends where the footer
    /// begins, so the last one or two rows the managed side sliced were not drawn. They are taken
    /// off here instead, counted by the window's last draw, when that was at this size.
    func grid(for size: CGSize) -> (columns: Int32, rows: Int32) {
        var rows = Int(size.height / cell.height)
        if let room, room.size == size {
            rows = min(rows, room.rows + Renderer.chromeRows)
        }

        return (Int32(max(0, size.width - rightInset) / cell.width), Int32(rows))
    }

    /// `capturing` decodes and scales pictures here and now, and stands a spinner still: a capture
    /// draws one frame, which has to have its pictures in it and come out the same every time.
    ///
    /// The layout that comes back is the whole window's whatever is being repainted. What a
    /// repaint of part of it leaves out is the drawing, and a capture leaves out nothing.
    @discardableResult
    func draw(_ frame: Frame, in context: CGContext, size: CGSize, capturing: Bool = false) -> Layout {
        let landed = settle()
        dirty = capturing ? nil : context.boundingBoxOfClipPath
        // Something landed in a draw that is not the whole window's, so the window is owed one.
        // A turn of a spinner is clipped to the spinner, and the picture that landed as it began
        // was drawn inside that clip and nowhere else: `Runtime.present` had already asked whether
        // anything landed, been told no, and would not be told again. A capture is no draw of the
        // window's at all. Only said here, and asked for by the next present, so a draw never
        // asks for another from inside itself.
        if landed, capturing || dirty?.contains(CGRect(origin: .zero, size: size)) != true {
            owed = true
        }

        // The lines the last draw drew are the ones this one may use again, and what the draw
        // before it drew and it did not goes here. A draw that reached no text is passed over:
        // where the clip is a spinner, a turn would otherwise leave nothing kept for whatever is
        // drawn after it.
        if !lines.isEmpty {
            earlier = lines
            lines = [:]
        }

        lines.reserveCapacity(earlier.count)
        var layout = Layout()
        context.setFillColor(Palette.background)
        context.fill(CGRect(origin: .zero, size: size))

        let line = cell.height
        let hasQueue = !frame.queue.isEmpty
        // The scroller's strip comes off the panes before anything is measured, so widening it
        // narrows the diff rather than overlapping it.
        let content = size.width - rightInset
        let queue = hasQueue ? clamp(queueWidth, content) : 0
        let panesLeft = hasQueue ? Renderer.padding + queue + Renderer.gap : Renderer.padding
        let panesWidth = max(cell.width * 2, content - Renderer.padding - panesLeft)
        let half = (panesWidth / 2).rounded(.down)

        // A title that would run on under the subtitle stops a character short of it, where the
        // subtitle used to be drawn over whatever of the title had got that far. It is the title
        // that gives way, as in the Linux head: what it says is also in the pane headers and the
        // queue, and which entry of the queue this is is said only by the subtitle. One that fits
        // has the whole row, as it always had, so it is drawn exactly as it was.
        let titleWidth = size.width - Renderer.padding * 2
        let subtitleWidth = CGFloat(frame.subtitle.count) * cell.width
        let runsUnder = !frame.subtitle.isEmpty && CGFloat(frame.title.count) * cell.width > titleWidth - subtitleWidth
        let titleRoom = runsUnder ? titleWidth - subtitleWidth - cell.width : titleWidth
        text(frame.title, in: rect(top: Renderer.padding, left: Renderer.padding, width: titleRoom, height: line, size), Palette.text, context)
        if !frame.subtitle.isEmpty {
            text(frame.subtitle, in: rect(top: Renderer.padding, left: size.width - Renderer.padding - subtitleWidth, width: subtitleWidth, height: line, size), Palette.dim, context)
        }

        let firstRule = Renderer.padding + line + Renderer.gap
        rule(top: firstRule, width: size.width, in: context, size)

        let headerTop = firstRule + Renderer.gap
        if hasQueue {
            // The count the managed side carries, not one derived from `queue`: that is the
            // visible slice, so thirty pending in a sixteen row body read as "Pending (16)" beside
            // "inline 1 of 30", and folding a group lowered it further.
            text("Pending (\(frame.pendingCount))", in: rect(top: headerTop, left: Renderer.padding, width: queue, height: line, size), Palette.text, context)
        }

        text(frame.left.header, in: rect(top: headerTop, left: panesLeft, width: half, height: line, size), Palette.text, context)
        text(frame.right.header, in: rect(top: headerTop, left: panesLeft + half, width: half, height: line, size), Palette.text, context)
        rule(top: headerTop + line + Renderer.gap, width: size.width, in: context, size)

        let bodyTop = Renderer.padding + (line + Renderer.gap) * 2 + Renderer.gap * 2
        // Before the body, which ends where the footer begins. The footer is as tall as its
        // buttons take, and that is more than one row of them once they are wider than the window.
        let placed = place(frame, width: size.width, line: line)
        let fits = Int(max(0, size.height - bodyTop - placed.height - Renderer.padding) / line)
        let capacity = max(1, fits)
        // What `grid` holds the rows it reports to. Not a capture's, which is drawn at a size of
        // its own and is no window.
        if !capturing {
            room = (size: size, rows: fits)
        }

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

        // Only the pictures this frame names stay decoded. An entry used to go only when its own
        // path was asked for again and had changed or gone, so every image reviewed in a session
        // was held until the session ended.
        let shown: Set<String> = [frame.left.imagePath, frame.right.imagePath]
        shared = !frame.left.imagePath.isEmpty && frame.left.imagePath == frame.right.imagePath
        pictures = pictures.filter { shown.contains($0.key) }
        unplaced = [:]
        gate.lock()
        wanted = shown
        gate.unlock()

        // Under the rows rather than instead of them. The rows are what every head draws — format,
        // size and byte count, coloured against the other side — and this one can afford to also
        // show the thing they describe.
        image(frame.left, left: panesLeft, width: half, top: bodyTop, bottom: bodyBottom, line: line, capturing: capturing, in: context, size, &layout)
        image(frame.right, left: panesLeft + half, width: panesWidth - half, top: bodyTop, bottom: bodyBottom, line: line, capturing: capturing, in: context, size, &layout)

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

        layout.body = rect(
            top: bodyTop,
            left: Renderer.padding,
            width: content - Renderer.padding * 2,
            height: bodyBottom - bodyTop,
            size)
        let gutter = Renderer.gutterCells * cell.width
        layout.panes = [
            PaneColumn(cellLeft: panesLeft, textLeft: panesLeft + gutter, width: half),
            PaneColumn(
                cellLeft: panesLeft + half,
                textLeft: panesLeft + half + gutter,
                width: panesWidth - half)
        ]
        layout.buttons = footer(frame, placed, size: size, line: line, in: context)
        return layout
    }

    /// Where the footer's buttons go and how tall that makes it. Worked out before anything is
    /// drawn, because the body ends where the footer begins.
    private struct Footer {
        /// One for each of the frame's buttons, in their order.
        var slots: [Slot] = []

        /// How many rows the buttons take: one, unless they are wider than the window.
        var rows = 1

        /// The status has a line of its own above the buttons, for want of room beside them.
        var statusAbove = false

        var height: CGFloat = 0

        struct Slot {
            var row = 0
            var left: CGFloat = 0
            var width: CGFloat = 0
        }
    }

    /// Lays the footer out: the buttons left to right, onto another row where the next would pass
    /// the window's edge, and the status right of the last of them where there is room for it.
    ///
    /// One row used to be all there was. A paged document has eleven buttons, wider together than
    /// the window opens, so the last of them were off it and could not be clicked. And the status
    /// was drawn from the right edge whatever was already there, which for an image pair in a
    /// queue was the last button. A status with no room beside the buttons has a line of its own
    /// instead, because it is where the page, the zoom, a selection and a failed accept are said.
    ///
    /// Where a button goes turns on the buttons and the window's width and nothing else. Not on
    /// the status, which changes while the pointer is on its way to a button: its line is above
    /// the buttons for that reason, so they stay where they are as it comes and goes.
    private func place(_ frame: Frame, width: CGFloat, line: CGFloat) -> Footer {
        var placed = Footer()
        let edge = width - Renderer.padding
        var left = Renderer.padding
        var end = Renderer.padding
        for button in frame.buttons {
            let span = CGFloat(button.label.count + 4) * cell.width
            // Never the first of its row, which has nowhere better to go however wide it is
            if left > Renderer.padding, left + span > edge {
                placed.rows += 1
                left = Renderer.padding
            }

            placed.slots.append(Footer.Slot(row: placed.rows - 1, left: left, width: span))
            end = left + span
            left = end + Renderer.gap
        }

        // Room is room right up to the last button, with no gap asked for: a conflicted entry's
        // line count has always sat two points off its variant button, and reads.
        let statusWidth = CGFloat(frame.status.count) * cell.width
        placed.statusAbove = !frame.status.isEmpty && edge - statusWidth < end
        let row = line + Renderer.gap * 2
        placed.height = row * CGFloat(placed.rows) + Renderer.gap * CGFloat(placed.rows - 1)
        if placed.statusAbove {
            placed.height += line + Renderer.gap
        }

        return placed
    }

    private func footer(_ frame: Frame, _ placed: Footer, size: CGSize, line: CGFloat, in context: CGContext) -> [CGRect] {
        let top = size.height - placed.height - Renderer.padding
        rule(top: top - Renderer.gap, width: size.width, in: context, size)

        let height = line + Renderer.gap * 2
        let first = placed.statusAbove ? top + line + Renderer.gap : top
        var rects: [CGRect] = []
        for (button, slot) in zip(frame.buttons, placed.slots) {
            let bounds = rect(
                top: first + CGFloat(slot.row) * (height + Renderer.gap),
                left: slot.left,
                width: slot.width,
                height: height,
                size)
            rects.append(bounds)

            context.setFillColor(button.enabled ? Palette.buttonFace : Palette.buttonDisabled)
            context.fill(bounds)
            let label = bounds.insetBy(dx: cell.width * 2, dy: (height - line) / 2)
            text(button.label, in: label, button.enabled ? Palette.text : Palette.dim, context)
        }

        guard !frame.status.isEmpty else {
            return rects
        }

        let edge = size.width - Renderer.padding
        let width = CGFloat(frame.status.count) * cell.width
        if placed.statusAbove {
            // From the left edge once it is wider than the window, so that what is cut off is its
            // end, which is the end the text renderer loses.
            let left = max(Renderer.padding, edge - width)
            let above = rect(top: top, left: left, width: edge - left, height: line, size)
            text(frame.status, in: above, Palette.dim, context)
            return rects
        }

        // Beside the last row of buttons, against the right edge
        let last = first + CGFloat(placed.rows - 1) * (height + Renderer.gap)
        let beside = rect(top: last + (height - line) / 2, left: edge - width, width: width, height: line, size)
        text(frame.status, in: beside, Palette.dim, context)
        return rects
    }

    private func queueItem(_ frame: Frame, _ index: Int, _ bounds: CGRect, _ context: CGContext) {
        guard index < frame.queue.count, shows(bounds) else {
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

        // Left alone when none of it is being repainted. Asked of the row with its gutter, which a
        // pane narrower than one draws past the row's own edge.
        let width = Renderer.gutterCells * cell.width
        guard shows(CGRect(x: bounds.minX, y: bounds.minY, width: max(bounds.width, width), height: bounds.height)) else {
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

        // A folded row has no number, and printing the -1 standing in for one put it in the gutter.
        let number = row.lineNumber < 0 ? "" : String(row.lineNumber)
        let gutter = "\(Palette.marker(row.kind)) \(String(repeating: " ", count: max(0, 4 - number.count)))\(number)"

        // Behind the text rather than over it, and the text keeps its own colour: what kind of
        // change a line is has to survive being selected.
        if row.selectLength > 0 {
            context.setFillColor(Palette.selection)
            context.fill(
                CGRect(
                    x: bounds.minX + width + CGFloat(row.selectStart) * cell.width,
                    y: bounds.minY,
                    width: CGFloat(row.selectLength) * cell.width,
                    height: bounds.height)
                    .intersection(bounds))
        }

        text(gutter, in: CGRect(x: bounds.minX, y: bounds.minY, width: width, height: bounds.height), Palette.dim, context)

        // Each segment at its column rather than the row as one line, so a character Core Text
        // takes from a fallback font, at that font's width, moves nothing after it. A row of plain
        // text is one segment at column 0, drawn exactly as the whole row was.
        let segments = row.segments.isEmpty ? [Frame.Segment(text: row.text, column: 0)] : row.segments
        for segment in segments {
            let left = bounds.minX + width + CGFloat(segment.column) * cell.width
            guard left < bounds.maxX else {
                break
            }

            text(
                segment.text,
                in: CGRect(x: left, y: bounds.minY, width: bounds.maxX - left, height: bounds.height),
                Palette.foreground(row.kind),
                context)
        }
    }

    /// The picture a pane is, one blank line under its rows — the same placement the other two
    /// heads use.
    ///
    /// Fitted, and never enlarged past its own size: a snapshot is judged against the pixels it
    /// has, and an eight point icon stretched across a pane is an interpolation of them rather than
    /// a look at them. Scaled from the size the model carries rather than from the decoded image,
    /// so all three heads place a picture identically.
    ///
    /// A spinner instead while the picture is on its way: a document's page the managed side is
    /// still drawing, which it says with `imagePending`, or a picture being decoded or scaled here.
    private func image(
        _ pane: Frame.Pane,
        left: CGFloat,
        width: CGFloat,
        top: CGFloat,
        bottom: CGFloat,
        line: CGFloat,
        capturing: Bool,
        in context: CGContext,
        _ size: CGSize,
        _ layout: inout Layout
    ) {
        let hasPicture = !pane.imagePath.isEmpty && pane.imageWidth > 0 && pane.imageHeight > 0
        guard hasPicture || pane.imagePending else {
            return
        }

        let imageTop = top + CGFloat(pane.rows.count + 1) * line
        let available = CGSize(width: width - Renderer.gap, height: bottom - imageTop)
        guard available.width > 0, available.height > 0 else {
            if hasPicture {
                leftOut(pane.imagePath)
            }

            return
        }

        // Where the picture will be centred once it can be drawn, and so where a spinner stands in
        // for it until then
        let space = rect(top: imageTop, left: left, width: available.width, height: available.height, size)
        layout.pictures.append(PictureSpace(bounds: space))
        guard hasPicture else {
            spinner(in: space, line: line, capturing: capturing, context, &layout)
            return
        }

        let (decoded, loading) = self.picture(pane.imagePath, capturing: capturing)
        guard let picture = decoded else {
            // Nothing at all for a picture ImageIO cannot read: the rows have said what it is
            if loading {
                spinner(in: space, line: line, capturing: capturing, context, &layout)
            }

            return
        }

        let scale = min(
            min(
                available.width / CGFloat(pane.imageWidth),
                available.height / CGFloat(pane.imageHeight)),
            1)
        let drawn = CGSize(
            width: max(1, (CGFloat(pane.imageWidth) * scale).rounded(.down)),
            height: max(1, (CGFloat(pane.imageHeight) * scale).rounded(.down)))
        if pane.imageZoom > 1 {
            enlarged(pane, picture, fitted: drawn, top: imageTop, left: left, available: available, capturing: capturing, in: context, size, &layout)
            return
        }

        let bounds = rect(
            top: imageTop + ((available.height - drawn.height) / 2).rounded(.down),
            left: left + ((available.width - drawn.width) / 2).rounded(.down),
            width: drawn.width,
            height: drawn.height,
            size)

        let device = context.convertToDeviceSpace(bounds).size
        guard let scaled = self.fitted(pane.imagePath, picture, device: device, capturing: capturing) else {
            spinner(in: space, line: line, capturing: capturing, context, &layout)
            return
        }

        // Left alone when none of it is being repainted, as when the clip is the other pane's
        // spinner. Asked of the picture with its outline, which is the point outside it.
        guard shows(bounds.insetBy(dx: -1, dy: -1)) else {
            return
        }

        checker(bounds, in: context)

        context.saveGState()
        // A copy at another size, mid resize, is stretched into place until this size has been
        // made, and has to be quick about it rather than good: it is redrawn as soon as that lands
        let exact = scaled === picture ||
            (scaled.width == Int(abs(device.width).rounded()) && scaled.height == Int(abs(device.height).rounded()))
        context.interpolationQuality = exact ? .high : .medium
        context.draw(scaled, in: bounds)
        context.restoreGState()

        // An outline, so a picture whose edges are the colour of the pane still has visible extent.
        context.setStrokeColor(Palette.rule)
        context.setLineWidth(1)
        context.stroke(bounds.insetBy(dx: -0.5, dy: -0.5))
    }

    /// A picture the reader has zoomed into: `imageZoom` times the size that fits, cut off at the
    /// edges of the space under the rows rather than drawn over anything else. What shows is the
    /// part around the centre the managed side asked for, moved in as far as it takes to keep the
    /// space full: it does not know how many points a pane has, so it can ask for one at the edge.
    ///
    /// Past its own size it is drawn straight from the decoded picture, clipped, rather than from a
    /// copy scaled to that size, which at the last step would be hundreds of megabytes to show one
    /// corner of it. Below its own size a copy is about the size of the picture at the most, and
    /// the window draws from one: see `reduced`. A capture draws from the picture at any size, as
    /// it always has.
    private func enlarged(
        _ pane: Frame.Pane,
        _ picture: CGImage,
        fitted: CGSize,
        top: CGFloat,
        left: CGFloat,
        available: CGSize,
        capturing: Bool,
        in context: CGContext,
        _ size: CGSize,
        _ layout: inout Layout
    ) {
        let zoom = CGFloat(pane.imageZoom)
        let whole = CGSize(width: fitted.width * zoom, height: fitted.height * zoom)
        let shown = CGSize(
            width: min(available.width.rounded(.down), max(1, whole.width.rounded(.down))),
            height: min(available.height.rounded(.down), max(1, whole.height.rounded(.down))))
        let across = shown.width / whole.width
        let down = shown.height / whole.height
        let centre = CGPoint(
            x: min(max(CGFloat(pane.imageCenterX), across / 2), 1 - across / 2),
            y: min(max(CGFloat(pane.imageCenterY), down / 2), 1 - down / 2))
        let bounds = rect(
            top: top + ((available.height - shown.height) / 2).rounded(.down),
            left: left + ((available.width - shown.width) / 2).rounded(.down),
            width: shown.width,
            height: shown.height,
            size)

        // The whole picture, placed so the centre is at the middle of what shows. Nothing is
        // flipped, so its top is its maxY, and the centre's y is counted down from there.
        let all = CGRect(
            x: bounds.midX - centre.x * whole.width,
            y: bounds.midY - (1 - centre.y) * whole.height,
            width: whole.width,
            height: whole.height)

        // The last one appended is this pane's, by `image`, before it knew the picture was there.
        // Said before anything is drawn, because where the picture is does not turn on how much of
        // the window this draw is for.
        if !layout.pictures.isEmpty {
            layout.pictures[layout.pictures.count - 1].enlarged = true
            layout.pictures[layout.pictures.count - 1].whole = whole
            layout.pictures[layout.pictures.count - 1].centre = centre
            layout.pictures[layout.pictures.count - 1].across = across
            layout.pictures[layout.pictures.count - 1].down = down
            // What the frame carried, exactly, so that handing it back changes nothing. And a way
            // counts as one it can move only where the space is what cut it short: a picture a
            // fraction of a point wider than what shows of it has nowhere to go that can be seen.
            layout.pictures[layout.pictures.count - 1].asked = CGPoint(
                x: CGFloat(pane.imageCenterX),
                y: CGFloat(pane.imageCenterY))
            layout.pictures[layout.pictures.count - 1].movesAcross = whole.width.rounded(.down) > shown.width
            layout.pictures[layout.pictures.count - 1].movesDown = whole.height.rounded(.down) > shown.height
        }

        // Below its own size the window draws it from a copy scaled to the pixels the whole of it
        // takes: see `reduced`. Asked for here rather than past the test below, as a fitted
        // picture's copy is, so that a draw which leaves the picture out has still started the
        // copy the next one will want. Not for a capture, which is one frame with no later one
        // for a copy to land in, and not when both panes name the one picture.
        let device = context.convertToDeviceSpace(all).size
        let reducing = !capturing && !shared && abs(device.width) < CGFloat(picture.width)
        let made = reducing ? self.fitted(pane.imagePath, picture, device: device, capturing: false) : nil

        // Left alone when none of it is being repainted, as a fitted one is
        guard shows(bounds.insetBy(dx: -1, dy: -1)) else {
            return
        }

        checker(bounds, in: context)

        context.saveGState()
        context.clip(to: bounds)
        if reducing {
            reduced(made, picture, at: all, device: device, in: context)
        } else {
            // Its pixels as they are once it is past its own size, which is what zooming that far
            // in is for: smoothed, a one pixel difference between the two sides is a blur on both.
            context.interpolationQuality = abs(device.width) >= CGFloat(picture.width) ? .none : .high
            context.draw(picture, in: all)
        }

        context.restoreGState()

        context.setStrokeColor(Palette.rule)
        context.setLineWidth(1)
        context.stroke(bounds.insetBy(dx: -0.5, dy: -0.5))
    }

    /// The whole of an enlarged picture that is still below its own size, at `all`, for the
    /// window. The caller has clipped to the part of it that shows, and `made` is what `fitted`
    /// had for the pixels the whole of it takes.
    ///
    /// Drawn from a copy scaled to those pixels, made on `work` and kept as the fitted one is, in
    /// its place. Drawn from the picture itself, this was a resample at `.high` of every source
    /// pixel under the clip on every redraw, and a drag is a redraw a frame: both panes of a pair
    /// of screenshots, to move them. Where the picture has been dragged to is no part of the copy,
    /// so a drag is the same copy drawn somewhere else.
    ///
    /// Only where the whole of it is narrower than the picture, so the copy is about the picture's
    /// own size at the most. Until it lands the picture itself is drawn, quickly rather than well,
    /// and drawn again when it does.
    private func reduced(_ made: CGImage?, _ picture: CGImage, at all: CGRect, device: CGSize, in context: CGContext) {
        guard let scaled = made,
              scaled !== picture,
              scaled.width == Int(abs(device.width).rounded()),
              scaled.height == Int(abs(device.height).rounded())
        else {
            // The picture itself. As well as it ever was drawn when it is not one to make a copy
            // of, being within a pixel of its own size or having failed to scale. Quickly
            // otherwise: the copy for this size is on its way, and whatever `fitted` had to hand
            // back in the meantime was made for another.
            context.interpolationQuality = made === picture ? .high : .low
            context.draw(picture, in: all)
            return
        }

        // A pixel of the copy to a pixel of the screen. `all` starts wherever the drag left it,
        // which is seldom on a pixel, and is the copy's size only to the nearest one. Drawn into
        // `all` as it stands the copy would be sampled again on every frame: smoothed, each pane
        // softening its own by a different fraction of a pixel, or not, and losing or doubling a
        // row or a column of it where the two sizes part. So it goes on the pixel nearest to where
        // `all` starts, at its own size, which leaves every part of it within about a pixel of
        // where `all` has it.
        let target = context.convertToDeviceSpace(all)
        let placed = context.convertToUserSpace(
            CGRect(
                x: Renderer.pixel(target.minX),
                y: Renderer.pixel(target.minY),
                width: CGFloat(scaled.width),
                height: CGFloat(scaled.height)))
        context.interpolationQuality = .none
        context.draw(scaled, in: placed)
    }

    /// The pixel a device coordinate is put on: the nearest, with two things seen to first.
    ///
    /// Where a picture has been dragged to goes to the managed side and back as a Float, which
    /// leaves a coordinate a few ten thousandths of a pixel either side of where it should be,
    /// and where it should be is often exactly half way between two pixels. So it is taken to the
    /// nearest eighth of a pixel first. And a half goes up whatever its sign, where `rounded()`
    /// takes it away from zero and so the other way once a picture's edge has left the window.
    /// Without the two, a picture dragged a pixel at a time moved by none or by two.
    private static func pixel(_ value: CGFloat) -> CGFloat {
        ((value * 8).rounded() / 8 + 0.5).rounded(.down)
    }

    /// Something turning, centred in `space`, while the picture that will be centred there is on
    /// its way: a dim ring, and a brighter quarter of it going round once a second, as the other two
    /// heads draw it. Stood still in a capture, which has to come out the same every time, and left
    /// out of a space too small to hold it. Where it went is added to `layout`, which is what
    /// `Runtime` redraws to turn it.
    private func spinner(in space: CGRect, line: CGFloat, capturing: Bool, _ context: CGContext, _ layout: inout Layout) {
        let radius = line.rounded(.down)
        let thickness = max(2, (line / 6).rounded(.down))
        guard space.width >= (radius + thickness) * 2,
              space.height >= (radius + thickness) * 2
        else {
            return
        }

        let centre = CGPoint(x: space.midX.rounded(.down), y: space.midY.rounded(.down))
        let ring = CGRect(x: centre.x - radius, y: centre.y - radius, width: radius * 2, height: radius * 2)
        let turned = capturing
            ? 0
            : CGFloat(ProcessInfo.processInfo.systemUptime.truncatingRemainder(dividingBy: 1)) * 2 * .pi

        context.saveGState()
        context.setLineWidth(thickness)
        context.setStrokeColor(Palette.rule)
        context.strokeEllipse(in: ring)
        // From twelve o'clock, clockwise. Nothing is flipped here, so twelve o'clock is a half pi
        // and clockwise is a falling angle.
        context.setStrokeColor(Palette.dim)
        context.setLineCap(.round)
        context.addArc(center: centre, radius: radius, startAngle: .pi / 2 - turned, endAngle: -turned, clockwise: true)
        context.strokePath()
        context.restoreGState()

        layout.spinners.append(ring.insetBy(dx: -(thickness + 1), dy: -(thickness + 1)))
    }

    private func checker(_ bounds: CGRect, in context: CGContext) {
        context.setFillColor(Palette.checkerLight)
        context.fill(bounds)
        context.setFillColor(Palette.checkerDark)

        var row = 0
        var y = bounds.minY
        while y < bounds.maxY {
            var column = 0
            var x = bounds.minX
            while x < bounds.maxX {
                if row % 2 != column % 2 {
                    let square = CGRect(x: x, y: y, width: Renderer.checkerSize, height: Renderer.checkerSize)
                    context.fill(square.intersection(bounds))
                }

                x += Renderer.checkerSize
                column += 1
            }

            y += Renderer.checkerSize
            row += 1
        }
    }

    /// `picture` scaled down to `device` pixels, kept until the size or the picture changes, or
    /// `picture` itself where it is not being scaled down: drawing at or above its own size costs
    /// little, and drawing the original there leaves what a capture shows exactly as it was.
    ///
    /// For the window the copy is made on `work`, and meanwhile this is the copy made for the size
    /// the pane last had, for the caller to stretch into place, or nil when there has never been
    /// one, which the pane shows a spinner for.
    ///
    /// `device` is the size the picture fills when it is fitted, and the size the whole of it
    /// takes when `enlarged` asks, for one that is still below its own size. Either way it is the
    /// one copy, so going from one to the other makes it again. Nil is no spinner there: `reduced`
    /// draws the picture itself until the copy lands.
    private func fitted(_ path: String, _ picture: CGImage, device: CGSize, capturing: Bool) -> CGImage? {
        let width = Int(abs(device.width).rounded())
        let height = Int(abs(device.height).rounded())
        guard width > 0,
              height > 0,
              width < picture.width || height < picture.height,
              var entry = pictures[path],
              !entry.unscalable
        else {
            return picture
        }

        if let scaled = entry.scaled,
           scaled.width == width,
           scaled.height == height {
            return scaled
        }

        // The other of the two copies a picture in both panes keeps, which is this pane's when
        // the last one to land was made for the other
        if let spare = entry.spare,
           spare.width == width,
           spare.height == height {
            return spare
        }

        let size = Pixels(width: width, height: height)
        if capturing {
            guard let scaled = Renderer.scale(picture, to: size) else {
                entry.unscalable = true
                pictures[path] = entry
                return picture
            }

            entry.scaled = scaled
            pictures[path] = entry
            return scaled
        }

        if entry.scaling == nil {
            entry.scaling = size
            pictures[path] = entry
            let (modified, length) = (entry.modified, entry.length)
            work.async { [weak self] in
                guard let self, self.isWanted(path) else {
                    return
                }

                let scaled = Renderer.scale(picture, to: size)
                self.post(.scaled(path: path, modified: modified, length: length, size: size, image: scaled))
            }
        }

        return entry.scaled
    }

    private static func scale(_ picture: CGImage, to size: Pixels) -> CGImage? {
        guard let space = CGColorSpace(name: CGColorSpace.sRGB),
              let bitmap = CGContext(
                  data: nil,
                  width: size.width,
                  height: size.height,
                  bitsPerComponent: 8,
                  bytesPerRow: 0,
                  space: space,
                  bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)
        else {
            return nil
        }

        bitmap.interpolationQuality = .high
        bitmap.draw(picture, in: CGRect(x: 0, y: 0, width: size.width, height: size.height))
        return bitmap.makeImage()
    }

    /// Whether the window has something new to draw all of itself for: a picture or a scaled copy
    /// that has landed since this was last asked, whether it was put in place here or by a draw
    /// that got to it first and could only paint part of the window. Asked by `Runtime.present`,
    /// once a frame.
    func takeFinished() -> Bool {
        let landed = settle()
        let late = owed
        owed = false
        return landed || late
    }

    /// Puts what `work` has finished into the pictures still waiting for it, and says whether
    /// anything landed, which is a reason to redraw. A result for a picture that has left the
    /// screen since, or for a file rewritten since, is dropped.
    private func settle() -> Bool {
        gate.lock()
        let landed = finished
        finished = []
        gate.unlock()

        var changed = false
        for result in landed {
            switch result {
            case let .decoded(path, modified, length, image):
                guard var entry = pictures[path],
                      entry.decoding,
                      entry.modified == modified,
                      entry.length == length
                else {
                    continue
                }

                entry.decoding = false
                entry.image = image
                pictures[path] = entry
                changed = true
            case let .scaled(path, modified, length, size, image):
                guard var entry = pictures[path],
                      entry.scaling == size,
                      entry.modified == modified,
                      entry.length == length
                else {
                    continue
                }

                entry.scaling = nil
                if let image {
                    // The copy this one takes the place of is the other pane's, when both panes
                    // name the picture, and is kept for it. Otherwise it was made for a size
                    // the pane no longer has, and goes.
                    entry.spare = shared ? entry.scaled : nil
                    entry.scaled = image
                } else {
                    entry.unscalable = true
                }

                pictures[path] = entry
                changed = true
            }
        }

        return changed
    }

    private func isWanted(_ path: String) -> Bool {
        gate.lock()
        defer {
            gate.unlock()
        }

        return wanted.contains(path)
    }

    private func post(_ result: Finished) {
        gate.lock()
        finished.append(result)
        gate.unlock()
    }

    /// Whether a picture `frame` shows is not the one last drawn: its file was rewritten, has
    /// appeared, or has gone. `Runtime.present` redraws on this as well as on a changed frame,
    /// since a re-run can rewrite a received image with the same size and dimensions, which is an
    /// identical frame.
    func picturesChanged(_ frame: Frame) -> Bool {
        for pane in [frame.left, frame.right] {
            guard !pane.imagePath.isEmpty,
                  pane.imageWidth > 0,
                  pane.imageHeight > 0
            else {
                continue
            }

            // As the last draw saw the file: decoded or on its way, or passed over for want of room
            let seen = pictures[pane.imagePath].map { Stamp(modified: $0.modified, length: $0.length) }
                ?? unplaced[pane.imagePath]
            let attributes = try? FileManager.default.attributesOfItem(atPath: pane.imagePath)
            guard let modified = attributes?[.modificationDate] as? Date,
                  let length = attributes?[.size] as? UInt64
            else {
                // Gone: worth a redraw only to take away what was drawn
                if seen != nil {
                    return true
                }

                continue
            }

            guard let seen else {
                return true
            }

            if seen.modified != modified || seen.length != length {
                return true
            }
        }

        return false
    }

    /// Notes a picture this draw has no room for, so that `picturesChanged` knows it was seen and
    /// asks for a redraw only when its file is written again. A copy decoded from the file as it
    /// was before goes, since it is stale and would otherwise say so on every frame.
    private func leftOut(_ path: String) {
        guard let attributes = try? FileManager.default.attributesOfItem(atPath: path),
              let modified = attributes[.modificationDate] as? Date,
              let length = attributes[.size] as? UInt64
        else {
            return
        }

        unplaced[path] = Stamp(modified: modified, length: length)
        if let cached = pictures[path], cached.modified != modified || cached.length != length {
            pictures.removeValue(forKey: path)
        }
    }

    /// The decoded picture at `path`, and whether it is still on its way: being decoded on `work`,
    /// which the pane shows a spinner for. Nil and not loading is a file ImageIO cannot read.
    private func picture(_ path: String, capturing: Bool) -> (image: CGImage?, loading: Bool) {
        guard let attributes = try? FileManager.default.attributesOfItem(atPath: path),
              let modified = attributes[.modificationDate] as? Date,
              let length = attributes[.size] as? UInt64
        else {
            pictures.removeValue(forKey: path)
            return (nil, false)
        }

        // A capture does not wait on a decode the window started: it cannot.
        if let cached = pictures[path],
           cached.modified == modified,
           cached.length == length,
           !(cached.decoding && capturing) {
            return (cached.image, cached.decoding)
        }

        if capturing {
            let image = Renderer.decode(path)
            pictures[path] = Picture(image: image, modified: modified, length: length)
            return (image, false)
        }

        pictures[path] = Picture(image: nil, modified: modified, length: length, decoding: true)
        work.async { [weak self] in
            guard let self, self.isWanted(path) else {
                return
            }

            let image = Renderer.decode(path)
            self.post(.decoded(path: path, modified: modified, length: length, image: image))
        }

        return (nil, true)
    }

    /// ImageIO rather than NSImage, which would hand back a representation sized for a screen when
    /// what this wants is the file's own pixels. It reads every format the viewer compares.
    ///
    /// Decoded now, on whichever thread asked. ImageIO otherwise hands back an image that decodes
    /// itself the first time it is drawn, which is on the main thread, and is the slow part.
    private static func decode(_ path: String) -> CGImage? {
        guard let source = CGImageSourceCreateWithURL(URL(fileURLWithPath: path) as CFURL, nil),
              CGImageSourceGetCount(source) > 0
        else {
            return nil
        }

        let options = [kCGImageSourceShouldCacheImmediately: true] as CFDictionary
        return CGImageSourceCreateImageAtIndex(source, 0, options)
    }

    /// Clipped to its own rect, so a long line stops at its column instead of running into the
    /// next one. Which is also what lets it be left out when that rect is not being repainted:
    /// none of it could have landed anywhere else.
    private func text(_ string: String, in bounds: CGRect, _ colour: CGColor, _ context: CGContext) {
        guard !string.isEmpty, bounds.width > 0, shows(bounds) else {
            return
        }

        context.saveGState()
        context.clip(to: bounds)
        context.textPosition = CGPoint(x: bounds.minX, y: bounds.minY + descent)
        CTLineDraw(typeset(string, colour), context)
        context.restoreGState()
    }

    /// `string` as a line in `colour`: the one this draw or the last one drew it with, or else a
    /// new one, as every line used to be.
    private func typeset(_ string: String, _ colour: CGColor) -> CTLine {
        let flat = RowText.flatten(string)
        let key = LineKey(text: flat, colour: colour)
        if let kept = lines[key] {
            return kept
        }

        let line = earlier[key] ?? lay(flat, colour)
        lines[key] = line
        return line
    }

    private func lay(_ flat: String, _ colour: CGColor) -> CTLine {
        let attributed = NSAttributedString(
            string: flat,
            attributes: [
                .font: font,
                .foregroundColor: colour
            ])
        return CTLineCreateWithAttributedString(attributed)
    }

    /// Whether anything drawn in `bounds` can reach the screen in the draw in progress: always in
    /// a capture, and in a window when what is being repainted touches `bounds`, by however
    /// little.
    ///
    /// Asked only with a rect that holds everything the caller would paint. The background, the
    /// rules, a button's face and a spinner are not asked about at all: each is a call or two, and
    /// the clip sees to them. And asked only about drawing: where things are goes into the layout
    /// whatever is being repainted, since the view resolves clicks against it afterwards.
    private func shows(_ bounds: CGRect) -> Bool {
        guard let dirty else {
            return true
        }

        return dirty.intersects(bounds)
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
