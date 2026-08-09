import CDeview
import CoreGraphics

/// A plain C enum arrives in Swift as a struct whose rawValue is unsigned, while every field that
/// holds one across the ABI is `int32_t`. These do the conversion once rather than at every
/// comparison.
extension DeviewRowKind {
    var value: Int32 { Int32(rawValue) }
}

extension DeviewKey {
    var value: Int32 { Int32(rawValue) }
}

extension DeviewQueueFlags {
    var value: Int32 { Int32(rawValue) }
}

extension DeviewButtonFlags {
    var value: Int32 { Int32(rawValue) }
}

/// Transcribed from `RowColour` and `RowBackground` in deview.cpp, so a change looks the same
/// whichever renderer drew it. The screen model carries a row kind and never a colour, so this
/// mapping belongs to each renderer rather than to the model.
enum Palette {
    static let background = grey(24)
    static let filler = grey(28)
    static let text = grey(212)

    /// The gutter, and the subtitle and status that the other heads draw dimmed.
    static let dim = grey(130)

    static let rule = grey(70)

    /// ImGui draws a selected item as its accent at 31% over the window background. This is that
    /// composite, so the queue highlight matches without carrying an alpha channel around.
    static let selected = rgb(38, 64, 90)

    static let buttonFace = grey(52)
    static let buttonDisabled = grey(34)

    static func foreground(_ kind: Int32) -> CGColor {
        switch kind {
        case DEVIEW_ROW_ADDED.value:
            return rgb(126, 214, 139)
        case DEVIEW_ROW_REMOVED.value:
            return rgb(233, 129, 129)
        case DEVIEW_ROW_MODIFIED.value:
            return rgb(231, 197, 113)
        default:
            return text
        }
    }

    /// Nil where the row takes the window background, which is every unchanged row.
    static func rowBackground(_ kind: Int32) -> CGColor? {
        switch kind {
        case DEVIEW_ROW_ADDED.value:
            return rgb(38, 74, 44)
        case DEVIEW_ROW_REMOVED.value:
            return rgb(84, 40, 40)
        case DEVIEW_ROW_MODIFIED.value:
            return rgb(74, 64, 32)
        case DEVIEW_ROW_FILLER.value:
            return filler
        default:
            return nil
        }
    }

    static func marker(_ kind: Int32) -> String {
        switch kind {
        case DEVIEW_ROW_ADDED.value:
            return "+"
        case DEVIEW_ROW_REMOVED.value:
            return "-"
        case DEVIEW_ROW_MODIFIED.value:
            return "~"
        default:
            return " "
        }
    }

    private static func rgb(_ red: Int, _ green: Int, _ blue: Int) -> CGColor {
        CGColor(
            srgbRed: CGFloat(red) / 255,
            green: CGFloat(green) / 255,
            blue: CGFloat(blue) / 255,
            alpha: 1)
    }

    private static func grey(_ level: Int) -> CGColor {
        rgb(level, level, level)
    }
}
