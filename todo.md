# Review todo

Open findings from a review of `main` at 4244ebe6 (2026-09-23), rechecked on c37bf9e1. Done items are removed.

- **repro**: a test on the local branch `review-repros` fails on c37bf9e1.
- **verified**: confirmed by reading the code (or running the API involved), no test yet.
- **cannot verify here**: needs a platform this machine lacks; the item says what would settle it.


## Bugs

The repro tests are on the local branch `review-repros`, one class per area: `ReviewReproWindowsTests`, `ReviewReproTrayTests`, `ReviewReproPatcherTests`, `ReviewReproLibraryTests`. The fixed ones have moved into the topic test classes. Each test fails on c37bf9e1 except a control (`ControlSpaceIndentedLocalLeavesTheSiblingAlone`) and a measurement (`HowLongADecodeHoldsTheFile`).

Viewer model

- [ ] **Selection columns count one code point to a cell, which wide CJK and combining marks do not take** (verified)
  - Columns now count code points, which fixed the copy and the highlight for characters outside the basic plane: GDI+ draws those one cell wide and ImGui lays out one glyph per code point (`SelectionText.Cells`). Still off: CJK falls back to a font 1.83 cells wide on Windows, a combining mark takes none, and Core Text substitutes fonts with their own widths.
  - Fix: have each head report string positions from its own layout rather than cells, or put every code point on the grid.


Native

- [ ] **macOS App Nap can stall the loop while the window is covered** (cannot verify here)
  - Nothing opts out (no `beginActivity`, `NSAppSleepDisabled` or power assertion), the only wait is `nextEvent(until: now + 1/60)` (`Runtime.swift:248-253`), and a Focus queued by an arriving patch waits for the next managed frame.
  - Check on a Mac: cover the viewer for a minute, confirm Activity Monitor shows App Nap, then time how long a failing inline test takes to bring it forward against an uncovered window.
