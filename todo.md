# Review todo

Open findings from a review of `main` at 4244ebe6 (2026-09-23), rechecked on c37bf9e1. Done items are removed.

- **repro**: a test on the local branch `review-repros` fails on c37bf9e1.
- **verified**: confirmed by reading the code (or running the API involved), no test yet.
- **cannot verify here**: needs a platform this machine lacks; the item says what would settle it.


## Bugs

The repro tests are on the local branch `review-repros`, one class per area: `ReviewReproWindowsTests`, `ReviewReproTrayTests`, `ReviewReproPatcherTests`, `ReviewReproLibraryTests`. The fixed ones have moved into the topic test classes; the settle-by-member repros are still only there. Each test fails on c37bf9e1 except a control (`ControlSpaceIndentedLocalLeavesTheSiblingAlone`) and a measurement (`HowLongADecodeHoldsTheFile`).

Viewer model

- [ ] **Selection columns count one code point to a cell, which wide CJK and combining marks do not take** (verified)
  - Columns now count code points, which fixed the copy and the highlight for characters outside the basic plane: GDI+ draws those one cell wide and ImGui lays out one glyph per code point (`SelectionText.Cells`). Still off: CJK falls back to a font 1.83 cells wide on Windows, a combining mark takes none, and Core Text substitutes fonts with their own widths.
  - Fix: have each head report string positions from its own layout rather than cells, or put every code point on the grid.


Native

- [ ] **macOS App Nap can stall the loop while the window is covered** (cannot verify here)
  - Nothing opts out (no `beginActivity`, `NSAppSleepDisabled` or power assertion), the only wait is `nextEvent(until: now + 1/60)` (`Runtime.swift:248-253`), and a Focus queued by an arriving patch waits for the next managed frame.
  - Check on a Mac: cover the viewer for a minute, confirm Activity Monitor shows App Nap, then time how long a failing inline test takes to bring it forward against an uncovered window.

Library and inline

- [ ] **With no tray, `dotnet test` does not return until the viewer it launched is closed** (repro)
  - `src/DiffEngine/Viewer/ViewerLauncher.cs:104-115` starts the viewer with `UseShellExecute = false`, so it inherits every inheritable handle, including the pipe `dotnet test` reads the test host's output from. `DiffRunner` launches tools with the default `UseShellExecute = true` (`Definition.cs:13`), which inherits nothing.
  - Measured with a test starting a 22 s child each way, on Windows: MTP 23.4 s and VSTest 23.7 s with `UseShellExecute = false`, with or without redirecting stdin; 1.5 s and 1.3 s with ShellExecute. The scratch projects are not in the repo.
  - Redirecting all three handles still blocked. The stdin launch cannot use ShellExecute: it needs `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`, the payload sent another way, or a launcher that re-spawns itself detached.

- [ ] **Settle-by-member counts queue entries, not call sites** (repro)
  - A settle carries the passing call's own line, the framework and the member (`src/DiffEngine/DiffRunner_Inline.cs:130-139`). When the line names nothing, `FindByMember` removes the one entry with that member (`InlineQueue.cs:235-239`, `:304-331`), which may be a failing sibling's; `InlineStaging.cs:118-128` does the same on disk.
  - Tests: `SettleFromAPassingSiblingKeepsTheFailingSiblingsEntry`, `ClearFromAPassingSiblingKeepsTheFailingSiblingsStagedTrio`.
  - In Verify's usual shape the failing check throws first and the passing one never runs, so mostly it costs a drop and re-add. The entry is lost when the failing check does not end the test, or when the sibling is a same-named method in another nested class, run later.
  - Not fixable in the queue, which only sees failing call sites: `SettleFindsAnEntryWhoseLineHasMovedByMember` feeds the same inputs and wants the opposite. The settle has to carry the passing call's value or expression, or the owner has to re-locate the entry's call site.


## Perf

- [ ] macOS repaints the whole window every frame (`native/swift/Sources/Deview/Runtime.swift:139-140`). Redraw only when the frame, bounds or a picture stamp change, and cache scaled pictures.
- [ ] Windows image panes rescale from full resolution and redraw the checkerboard on every paint (11 to 40 ms per image), and decode on the UI thread (`ViewerCanvas.cs:385-420`, `ImageCache.cs:56-71`). Cache the composited scaled bitmap per path, stamp and size.
