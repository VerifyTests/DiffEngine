// The tray's library is PlatformHeads' PlatformHeadsMacTray and nothing else: its C ABI,
// platformheads_tray.h, which SwiftPM links into this product and so exports from it, and which
// PlatformHeads.Mac's StatusItemTray and StatusItemApp call. This target exists only to be the
// product's own, since a product can name no target of another package.
import PlatformHeadsMacTray
