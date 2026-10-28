import Foundation

/// A string handed back to the managed side: written to its buffer as UTF-8, as much as fits, and
/// its whole length returned, so a buffer too small for it is known to be.
enum Utf8Buffer {
    static func write(_ text: String, _ buffer: UnsafeMutablePointer<CChar>?, _ capacity: Int32) -> Int32 {
        let bytes = Array(text.utf8)
        let count = min(bytes.count, Int(capacity))
        if let buffer, count > 0 {
            bytes.withUnsafeBytes { source in
                _ = memcpy(buffer, source.baseAddress!, count)
            }
        }

        return Int32(bytes.count)
    }
}
