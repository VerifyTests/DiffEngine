# Review todo

Open findings from a review of `main` at 4244ebe6 (2026-09-23), rechecked on c37bf9e1. Done items are removed.

- **repro**: a test on the local branch `review-repros` fails on c37bf9e1.
- **verified**: confirmed by reading the code (or running the API involved), no test yet.
- **cannot verify here**: needs a platform this machine lacks; the item says what would settle it.


## Bugs

The repro tests are on the local branch `review-repros`, one class per area: `ReviewReproWindowsTests`, `ReviewReproTrayTests`, `ReviewReproPatcherTests`, `ReviewReproLibraryTests`. Each test fails on c37bf9e1 except a control (`ControlSpaceIndentedLocalLeavesTheSiblingAlone`) and a measurement (`HowLongADecodeHoldsTheFile`).

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

- [ ] **`MemberLine` takes the nearest same-named declaration, even below the hint** (repro)
  - Nearest by line distance, with no check of which span holds the hint (`src/DiffEngine/Inline/InlinePatcher.cs:1083-1120`). Two nested types each declaring `Works`: A's patch goes to B, or misses.
  - Tests: `ASameNamedMemberInTheNextNestedTypeDoesNotTakeThePatch`, `ASameNamedMemberInTheNextNestedTypeDoesNotHideTheCall`, `AnFsLocalNamedLikeTheTestDoesNotFloorTheSearchPastTheHint`.
  - Fix as suggested, tried: the three pass and the 225 patcher and applier tests still do. When several spans hold the hint, prefer the outermost.

- [ ] **`NextMemberLine` compares indentation by character count** (repro)
  - `InlinePatcher.cs:967`, `:980`. A tab-indented body under a space-indented member ends the member early, so the call is missed on every re-run, or the sibling above it is patched instead. C# only: F# rejects tabs (FS1161).
  - Tests: `ATabIndentedLocalDoesNotEndASpaceIndentedMember`, `ATabIndentedLocalDoesNotSendThePatchToTheSiblingAboveIt`, and the passing `ControlSpaceIndentedLocalLeavesTheSiblingAlone`, which differs only in indentation.
  - Tab stops of 4 fixed both; matching braces would be sturdier.

- [ ] **F#: a regular literal whose value looks like layout is AlreadyApplied forever** (repro)
  - Content holding `"""` is written as a regular literal (`src/DiffEngine/Inline/FsStringLiteral.cs:39-46`). The test library strips it as layout (`FsLanguage.cs:16-17`); the patcher does not (`FsStringLiteral.cs:127`, `:146`), finds it equal to the new content, and reports AlreadyApplied. The queue drops it, and the next run fails the same way.
  - Tests: `FsLayoutShapedRegularLiteralRoundTripsThroughTheCompiler` (through fsi), `FsPatcherDoesNotCallALayoutShapedRegularLiteralAlreadyApplied`.
  - Needs both halves, which together passed everything: the writer wraps layout-shaped fallback content in `"\n"…"\n"`, and the patcher strips regular and verbatim values as `SnapshotValue` does. Add the case to `FsCompilerRoundTripTests`.

- [ ] **F#: escapes F# does not define are rejected as "not a string literal"** (repro)
  - fsi keeps `"\d+"`, `"\0"`, `"\12"`, `"\e"`, `"\x4"`, `"\u12"` and `"\U0041"` literally, with no warning. `TryEscape` rejects them (`FsStringLiteral.cs:162-169`, `:188-201`), and the shared scanner rejects `\u`/`\U` before that (`StringLiteral.cs:445-461`), so an accept returns NotFound, on every run.
  - Tests: `FsUnknownEscapeIsKeptLiterally` (4 cases), `FsPatcherUpdatesALiteralHoldingAnUnknownEscape`.
  - Fixing `TryEscape` alone leaves `\u12`, and breaks `FsStringLiteralTests.ParseRejects` for `\0`, `\12` and `\e`, which pin the belief fsi disproves; they become `Parse` cases.

- [ ] **`WildcardFileFinder` throws out of `DiffTools`' static constructor under an undefined Program Files variable** (repro)
  - The unexpanded `%ProgramW6432%` becomes a relative root (`src/DiffEngine/WildcardFileFinder.cs:20-32`), and `DirectoryNotFoundException` escapes through `OsSettingsResolver.cs:163` to `DiffTools.cs:18`. With `ProgramW6432` unset, `DiffToolsTest` fails with a `TypeInitializationException`. ExamDiff (`ExamDiff.cs:40`) and Beyond Compare put a wildcard right after the variable.
  - Tests: `AnUndefinedVariableBeforeAWildcardIsNotFoundRatherThanThrown`, `ResolvingATwoLevelWildcardUnderAnUndefinedVariableIsNotFound`.
  - Only 64-bit Windows defines both variables, so this needs 32-bit Windows or a trimmed environment. Fix as suggested.

- [ ] **Sync `ViewerLaunchGate.Launch` deadlocks behind an async launch on a single threaded context** (repro)
  - `gate.Wait()` (`src/DiffEngine/Viewer/ViewerLaunchGate.cs:106`) blocks the thread that `LaunchAsync`'s awaits (`:162`, `:167`, `:214`) need to resume on.
  - Tests: `SyncLaunchBehindAnAsyncDeleteOnTheSameContextFinishes`, `SyncLaunchBehindAnAsyncInlineOnTheSameContextFinishes`.
  - Verify 33.1.1 calls only the async APIs, so this needs a sync caller (ApprovalTests or Shouldly style) in the same xUnit v2 assembly. Rare.
  - `ConfigureAwait(false)` in the gate fixes the delete case only: `ViewerLauncher.cs:28,30,32` resume on the caller's context too.

- [ ] **`InlineApplier.Apply` throws instead of returning `Failed`** (repro; the `PersistOwned` half was fixed by #878)
  - `OpenMutex` (`src/DiffEngine/Inline/InlineApplier.cs:410-413`) and `InlinePatcher.TryApply` (`:152-164`) are unguarded. In a viewer, a single or group accept then unwinds the loop: the queue is staged and the window vanishes mid review. Accept-all, the wire handler and the tray catch it.
  - Test: `AMutexThisProcessCannotOpenFailsTheApplyRatherThanThrowing` (`UnauthorizedAccessException` from a mutex an elevated process holds).

- [ ] **Staged file names over 255 characters are silently not written** (repro)
  - `BuildName` (`src/DiffEngine/Inline/InlineStaging.cs:423-428`) adds about 30 characters to the test name; the write throws and `:407-414` swallows it. Long path support does not help: the limit is the 255 character file name component.
  - Test: `ALongTestNameIsStillPersisted` (a 242 character test name).
  - Above about 225 characters of test name, which is mostly F# sentence names, or fewer non-ASCII ones on ext4 and APFS, whose limit is in bytes. Fix: truncate by UTF-8 bytes; the call-site hash keeps names unique.

- [ ] **Something other than a viewer on 3493 silently disables the viewer** (repro, against a fake upsd)
  - `TrySend` marks the port owned as soon as the connect succeeds (`src/DiffEngine/Protocol/ViewerClient.cs:233`), so an unparseable reply returns false (`:241`) and the unowned-port memory never applies. `IsOwned` stays true, so the gate never launches, `AddInlineAsync` returns `NoViewerFound`, a pair whose tool is the viewer gets `NoDiffToolFound`, deletes are dropped, and nothing mentions `DiffEngine_ViewerPort`.
  - Test: `ANonViewerOnThePortIsReportedRatherThanTakenForAnOwner`.
  - The connects cost under a millisecond each; losing the viewer is the problem. Real upsd was not tried: if it held the connection open, every send would wait out its timeout.


## Perf

- [ ] macOS repaints the whole window every frame (`native/swift/Sources/Deview/Runtime.swift:139-140`). Redraw only when the frame, bounds or a picture stamp change, and cache scaled pictures.
- [ ] Windows image panes rescale from full resolution and redraw the checkerboard on every paint (11 to 40 ms per image), and decode on the UI thread (`ViewerCanvas.cs:385-420`, `ImageCache.cs:56-71`). Cache the composited scaled bitmap per path, stamp and size.
