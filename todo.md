# Review todo

Open findings from a review of `main` at 4244ebe6 (2026-09-23), rechecked on c37bf9e1. Done items are removed.

- **repro**: a test on the local branch `review-repros` fails on c37bf9e1.
- **verified**: confirmed by reading the code (or running the API involved), no test yet.
- **cannot verify here**: needs a platform this machine lacks; the item says what would settle it.


## Bugs

The repro tests are on the local branch `review-repros`, one class per area: `ReviewReproWindowsTests`, `ReviewReproTrayTests`, `ReviewReproPatcherTests`, `ReviewReproLibraryTests`. The fixed ones have moved into the topic test classes. Each test fails on c37bf9e1 except a control (`ControlSpaceIndentedLocalLeavesTheSiblingAlone`) and a measurement (`HowLongADecodeHoldsTheFile`).

Nothing open.
