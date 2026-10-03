# macOS renderer

The macOS half of `libdiffengine_viewer`, drawn with AppKit and Core Text. It implements the same
ABI as `native/` does for Linux — `native/include/deview.h`, eleven exports over one flat frame
description — so the managed side is identical on both and `DiffEngineViewer.Core` has no idea
which one it loaded.

```
swift build -c release --arch arm64 --arch x86_64
```

Both `--arch` flags in one invocation give a universal binary, so there is no `lipo` step. Nothing
of the Swift runtime is shipped: it has been part of macOS since 10.14.4.

Built binaries are committed to `src/DiffEngineViewer.Mac/runtimes/{rid}/native/`, so a plain
`dotnet build` produces a shippable package and contributors never need Xcode. Regenerate them with
the `build-native` workflow, which opens a PR.

## Notes

`Sources/CDeview` exists only to import `deview.h`. Swift does not guarantee struct layout, so the
ABI structs have to come from the C header rather than being redeclared here. The header hides its
prototypes behind `DEVIEW_TYPES_ONLY`, because this library defines those symbols itself with
`@_cdecl`.

**C# owns the loop.** `deview_present` drains `NSApp.nextEvent` up to a deadline and returns,
rather than handing control to `NSApplication.run`. That is what keeps the scroll amplification,
the button lookup and the close-means-hide rule in `ViewerProgram` for every platform. It also
means the deadline is the frame throttle: without it the managed loop would spin a core.

**AppKit keeps loops of its own**, and each runs inside that pump, so the managed loop waits it
out. A menu while it is open, which is deliberate: that loop is what buys the keyboard, Escape and
VoiceOver. A window while it is being resized, which is not: the panes keep the rows they were
sliced for until the mouse comes up, drawn into the new size, and it takes a way to ask the managed
side for a frame from inside the loop to do better, which the ABI does not have. The scroller's
knob used to be a third. `PaneScroller` follows a drag of it as three ordinary events instead, so
the panes scroll as it moves.

**No app bundle.** `setActivationPolicy(.regular)` plus `finishLaunching()` is enough to get a
window that takes focus and appears in the dock, which is the same thing GLFW does for the Linux
build.

**Nothing is flipped.** Core Graphics has a bottom left origin; layout is written top down and
converted once, which avoids having to fight the text matrix to keep glyphs upright.

**Pictures come from ImageIO**, not `NSImage`, which would hand back a representation sized for a
screen when what the pane wants is the file's own pixels. They are cached and invalidated by write
time and length, the same freshness test the managed queue poller uses, because AppKit redraws for
a great many reasons and decoding per redraw would make an idle window a busy one. A file ImageIO
cannot read is remembered as a failure and draws nothing; the property rows above it still carry
the comparison, which is why those rows are the description and the picture is an addition to it.
This head is the only one of the three whose decoder reads every format the viewer compares.

**Pictures are decoded and scaled on a serial `DispatchQueue`**, not on the main thread, which is the
one the window answers on. Results are handed back through a lock-protected list rather than posted
to the main queue, so they land at a point this side chooses: `present` drains it, and redraws if
anything came. Until a picture lands, a spinner turns where it will be centred, redrawing only its
own rectangle; a capture decodes there and then and stands the spinner still, so a baseline holds.

**A hidden start creates no window.** `NSWindow` may only be instantiated on the main thread, and a
test host runs `[Before(Class)]` on whatever thread it likes, so `deview_init(hidden: 1)` builds
only the renderer and defers the window until `deview_set_hidden(0)` asks for one. The app always
starts visible, from `Main`, which is the main thread.

`deview_capture` draws into a bitmap context of its own making rather than asking the view for one.
`bitmapImageRepForCachingDisplay` would inherit the window's backing scale, so a committed baseline
would only match on the kind of display that produced it. Scale, colour space and the six font
smoothing and subpixel switches are all pinned there instead — which also means capture needs no
window server, so the snapshot tests are sturdier than the Linux ones.
