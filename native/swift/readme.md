# macOS renderer

The macOS half of `libdiffengine_viewer`, drawn with AppKit and Core Text. It implements the same
ABI as `native/` does for Linux — `native/include/deview.h`, eight exports over one flat frame
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

**No app bundle.** `setActivationPolicy(.regular)` plus `finishLaunching()` is enough to get a
window that takes focus and appears in the dock, which is the same thing GLFW does for the Linux
build.

**Nothing is flipped.** Core Graphics has a bottom left origin; layout is written top down and
converted once, which avoids having to fight the text matrix to keep glyphs upright.

`deview_capture` draws into a bitmap context of its own making rather than asking the view for one.
`bitmapImageRepForCachingDisplay` would inherit the window's backing scale, so a committed baseline
would only match on the kind of display that produced it. Scale, colour space and the six font
smoothing and subpixel switches are all pinned there instead — which also means capture needs no
window server, so the snapshot tests are sturdier than the Linux ones.
