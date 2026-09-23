# Review todo

Findings from a review of `main` at 4244ebe6 (2026-09-23).

- **repro**: a test in the appendix fails on that commit.
- **verified**: confirmed by reading the code (or running the API involved), no test yet.
- **unverified**: reported by a reviewer, not re-checked.


## Data loss

- [x] **Tray "Accept all" deletes verified files before applying inline patches** (verified)
  - **Fixed:** the menu, hot keys and wire accept-all now run moves, then snapshots, then deletes, and hold every delete when a snapshot in the batch was not written (`AcceptAllTally.Refused` in the owned host; read back from the full listing in `RemoteInlineHost`). The user is told why (`Tracker.DeletesHeld`). Tests: `TrackerDeleteTest.AcceptAll{AppliesTheSnapshotsBeforeTheDeletes,HoldsTheDeletesWhenASnapshotWasNotWritten}`, `OwnedInlineHostTest.AnAcceptAllWhose{PatchIsRefusedHoldsTheDeletes,PatchesAllLandCarriesOutTheDeletes}`, `TrackerTrackedFilesTest.AcceptAllHoldingDeletesLeavesThemPending`, `TrayViewerSyncTest.TrayAcceptAll{HoldsItsDeletesWhenTheOwningViewerRefusedAPatch,CarriesOutItsDeletesWhenTheOwningViewerWroteEveryPatch}`.
  - `src/DiffEngineTray/Tracker.cs:742` (`AcceptAll`, and `AcceptOpen` at :727) and `src/DiffEngineTray/OwnedInlineHost.cs:368` (`IQueueOwner.AcceptAll`, which an attached viewer's Accept all is forwarded to) sweep deletes, then moves, then snapshots, and never hold a delete.
  - A snapshot moving inline arrives as an Append patch plus a delete of `X.verified.txt`. If the Append comes back NotFound (source edited since the run, or a call site that cannot host `Snapshot`), the verified file is already gone and the literal was never written.
  - The viewer-owned batch does the opposite on purpose: snapshots first, deletes held once any snapshot was not written. Rationale on `InlineRefused` (`src/DiffEngineViewer/ViewerSession.cs:1101`), pinned by `HeldDeleteTests`. The tray is the default owner on Windows.
  - Fix: snapshots first, then moves; run deletes only if no non-conflicted snapshot in this batch was not written or failed, decided from `AcceptAllTally` rather than `queue.Count`. `OwnedInlineHostTest.AnAcceptAllCountsTheFilesIntoItsProgress` pins files-first incidentally.

- [x] **`AddInlineAsync` reports `Queued` when the viewer launch was capped** (verified)
  - **Fixed:** `DiffRunner.InlineResultFor` maps only `Launched` and `Taken` to `Queued`. Test: `ViewerLaunchGateTests.OnlyALaunchOrAHandoverIsQueued`. The related slot spent by a failed launch is not fixed (not data loss).
  - `src/DiffEngine/DiffRunner_Inline.cs:91`: `launched == ViewerLaunchOutcome.Failed ? NoViewerFound : Queued`. `Capped` (added in 26e79f0e, #855) falls through to `Queued`. `PendingFiles.Launched` already maps it correctly.
  - No tray, no viewer: five diff tools opened for file snapshots use up `MaxInstance`, and every later inline failure is capped, reported `Queued`, and not staged by the caller. With `DiffEngine_MaxInstances=0` that is every inline failure.
  - Fix: `Failed or Capped` → `NoViewerFound`.
  - Related: `MaxInstance.Reached()` increments before `launch()` runs, so a launch that returns false still spends a slot.

- [ ] **Committed Linux native binaries need glibc 2.38** (verified)
  - **Workflow fixed, binaries not yet rebuilt:** the Linux jobs now build inside `quay.io/pypa/manylinux_2_28_$(uname -m)` via `native/build-linux.sh`, and a "Check glibc floor" step fails the job if `objdump -T` shows anything above `GLIBC_2.28`. Not run locally (no Docker running here). Pushing the workflow change triggers `build-native`, whose propose job opens the PR with the rebuilt `.so` files; tick this once that merges.
  - Both `src/DiffEngineViewer.Linux/runtimes/linux-{x64,arm64}/native/libdiffengine_viewer.so` reference `GLIBC_2.38` (`__isoc23_sscanf`, `fmod`, `fmodf`), plus 2.35 and 2.34. They are built on `ubuntu-24.04` with no floor (`.github/workflows/build-native.yml:40`).
  - `dlopen` fails on Ubuntu 22.04 (2.35), Debian 12 (2.36), RHEL 8/9 and Amazon Linux 2023. CI only loads them on 24.04.
  - Fix: build in an old-glibc container (e.g. `quay.io/pypa/manylinux_2_28_*`) or with `zig cc -target x86_64-linux-gnu.2.28`, and fail the job if `objdump -T` shows a GLIBC version above the floor.

- [x] **A viewer that cannot open its window drops what it was launched with** (verified)
  - **Fixed:** `ViewerProgram.Run` persists on a window that will not open and in a `finally` around the loop; `RunInline` stages the patch when its forward is refused or fails; `ViewerLauncher` starts no viewer on Linux with neither `DISPLAY` nor `WAYLAND_DISPLAY`, so the caller hears `NoViewerFound` and stages. Tests: `ViewerProgramTests.AViewer{WithNoWindow,WhoseLoopThrows}StillStagesWhatItHolds`, `ViewerLauncherTests`. Not done: the launch gate still reports `Launched` for a process that exited during the bind wait (covered in practice by the viewer staging, except for a native crash).
  - `src/DiffEngineViewer/ViewerProgram.cs:210-215`: `Run` returns 4 before `PersistOwned`. `RunInline`/`RunDelete`/`RunDiff` bound the port first, so the launch gate saw an owner, reported `Launched`, and `AddInlineAsync` returned `Queued`. Any exception out of the loop does the same, since the outer catch at :41 also skips `PersistOwned`.
  - Hit by the glibc item above, and by any Linux machine with no display (SSH, devcontainer, WSL without WSLg). Nothing checks `DISPLAY`/`WAYLAND_DISPLAY` before launching.
  - Also: `WaitForBind` giving up after 5 s still returns `Launched` even when the process has already exited (`src/DiffEngine/Viewer/ViewerLaunchGate.cs:175-178`), and `RunInline`'s forward path ignores `response.Ok` (`ViewerProgram.cs:74`).
  - Fix: try/finally around everything after the bind that calls `PersistOwned`; have the launcher return the `Process` and report `Failed` once it has exited; consider not launching the viewer on Linux with no display.

- [x] **Re-applying an applied Set or Remove patch edits a different call** (repro)
  - **Fixed:** a Set stops at the recorded line once the call there already holds the new content (`InlinePatcher.IsOnHint`), and a Remove reports AlreadyApplied when the statement at the recorded line has no Snapshot left, including a chained line that was pulled up (`RemovedAtHint`). `RemoveWithNoSnapshotCall` now expects AlreadyApplied (renamed `RemoveWithNoSnapshotCallIsAlreadyDone`). Tests: `InlinePatcherTests.Reapplying*LeavesASiblingWithTheSameLiteral`, `RemoveWithNoCallAtAll`.
  - `src/DiffEngine/Inline/InlinePatcher.cs:147-167` (Set by expression), `:177-203` (Set by value), `:686-719` (`TryFindAnchoredCall`, used by Remove). When the hinted call no longer matches the anchor, the outward walk in `FindCalls` (:801) takes any other call whose literal still equals the old anchor, so the "another process may have applied it" branch (:166) is never reached.
  - Two `Verify(x).Snapshot("dup")` in one member, lines 5 and 6. Set with hint 6 makes line 6 "new". The same patch again returns `Applied` and makes line 5 "new" (should be `AlreadyApplied`). Remove twice strips both `Snapshot` calls.
  - Happens when a second target framework's identical patch arrives after the first was accepted (it becomes a new queue entry), or when each framework's test process applies the same Remove. Without MemberName the reach is file wide. `ReapplyingWithIdenticalLiteralsIsAlreadyApplied` does not cover it, since its anchor matches nothing.
  - Fix: before walking outward, if the recorded line is inside the member's span and its Snapshot value already equals `newContent`, return AlreadyApplied. For Remove, if that line holds an entry point with no Snapshot chained, report it as already removed.

- [x] **Tray accept-all applies patches from a copy of the queue taken at the start** (verified)
  - **Fixed:** `AcceptEvery` takes keys and re-finds each entry under the gate before applying it. Test: `OwnedInlineHostTest.AnEntryDiscardedDuringAnAcceptAllIsNotWritten`.
  - `src/DiffEngineTray/OwnedInlineHost.cs:527-541` (`AcceptEvery`) applies `entry.Patch` for every captured entry with no re-check, and `AcceptInBatch` then ignores the result because the entry changed.
  - Entries settled (the test now passes), discarded ("Discard (n)" mid-batch), replaced by a re-run, or made into a conflict during the batch are still written into source.
  - Fix: iterate keys; under `gate` re-find the entry, skip it when gone or conflicted, and apply the current patch outside the gate, the way `ViewerSession.ClaimNext` does. Testable with `HeldApply`.

- [x] **A failed `File.Replace` can delete the source file** (verified against the ReplaceFile docs, not reproduced)
  - **Fixed:** when the swap fails with the destination gone and the temporary present, the temporary is moved into place; the temporary is only deleted while the destination exists, and a failed recovery names where the source went. Tests: `InlineApplierTests.AReplaceThat{FailsAfterRemovingTheSourceStillLeavesOne,FailsCleanlyLeavesTheSourceAsItWas}`.
  - `src/DiffEngine/Inline/InlineApplier.cs:291-321`: `File.Replace(temporary, fullPath, null)`, then the `finally` deletes the temporary. ReplaceFile's `ERROR_UNABLE_TO_MOVE_REPLACEMENT` (1176) with no backup name leaves the original gone and the replacement under its temporary name, which the `finally` then deletes.
  - Triggered by anything holding the fresh temporary: antivirus, sync clients, network shares.
  - Fix: on failure, if `!File.Exists(fullPath) && File.Exists(temporary)`, move the temporary into place instead of deleting it.

- [x] **An item arriving just after the queue empties is acknowledged, then dropped** (repro)
  - **Fixed:** enqueues clear `Exit`; the loop commits to leaving under the lock (`ViewerSession.CommitExit` sets `SessionState.Closing`), as does every other way out of the loop; a closing viewer refuses Inline, Diff, Move and Delete, so the sender stages or reports instead. `TrackMove`/`TrackDelete` also read their files before taking the lock now. Tests: `ViewerSessionTests.{AnArrivalAfterTheQueueEmptiedKeepsTheWindow,NothingJoinsAQueueThatHasCommittedToLeaving,AnArrivalBeforeTheCommitCancelsIt}`, `IpcTests.AClosingViewerRefusesWhatWouldJoinTheQueue`.
  - `src/DiffEngineViewer/ViewerSession.cs:1395`: `Remove` sets `Exit = queue.Count == 0`, and `EnqueueInline` (:50) and `EnqueueTracked` (:141) carry `Exit` forward.
  - A settle empties the queue; before the next frame a Diff, Move or Delete arrives and is answered Ok; the loop then sees `Exit` and quits. `PersistOwned` stages only inline entries, so the tracked pair is in no window and no tray, and an inline one is staged rather than shown while its sender believes it is queued.
  - Fix: `Exit = false` in both enqueue methods. Better, decide the exit under the lock and have the handler refuse once closing.


## Bugs

- [x] **Attached viewer's right-click menu closes within 200 ms** (repro)
  - `src/DiffEngineViewer/ViewerSession.cs:189`: `Sync` always sets `Menu = null`. `OwnerLink.List` calls it on every poll (`src/DiffEngineViewer/Ipc/OwnerLink.cs:120`), `ViewerForm.ApplyMenu` then closes the popup, and a later click is dropped (`ViewerProgram.cs:385`). Every viewer is attached when the tray owns the queue, which is the default on Windows.
  - `EnqueueInline` also clears the menu when an identical snapshot is re-sent.
  - Fix: when the new queue is element-wise `ReferenceEquals` to `state.Queue` (`Project` and `ReadChanges` already reuse unchanged entries), keep `Menu`; if the message is null and progress unchanged too, return `state` itself. `AQueueChangeClosesTheMenu` only covers a listing that changed.

- [x] **.NET Framework: one quoted PATH entry breaks DiffTools for the whole process** (verified)
  - `src/DiffEngine/OsSettingsResolver.cs:14` splits PATH with no cleanup, and `:152` `Path.Combine("\"C:\\Program Files\\Foo\\bin\"", name)` throws `ArgumentException: Illegal characters in path` on net4x (checked in Windows PowerShell 5.1). It runs in `DiffTools`' static constructor, so it is a permanent `TypeInitializationException`, and `DiffRunner.Kill` throws on every passing test.
  - Fix: trim whitespace and quotes, and drop entries that are empty or contain invalid path characters.

- [x] **`DiffEngineViewer left right` never exits while the tray runs** (verified)
  - `src/DiffEngineViewer/ViewerProgram.cs:354`: hide-instead-of-exit has no mode check. File mode owns no port and the tray does not know the process, so X, Close, q and Esc hide it forever, and a blocking `git difftool` style caller hangs.
  - Fix: add `host.State.Mode == ViewerMode.Inline &&`.

- [x] **F# double-backtick test names get no MemberName narrowing** (repro)
  - `src/DiffEngine/Inline/InlinePatcher.cs:991-994` (`MemberLine`) and `src/DiffEngine/Inline/FsLanguage.cs:120` (`IsDeclaration`): for `let ``test b`` () =` the character before the name is a backtick, so it is not a declaration, `MemberLine` returns null, and the search is hint only. `NextMemberLine` never sees backticked siblings either. Double backticks are the common F# test naming style.
  - Repro: two tests ` ``test a`` ` and ` ``test b`` ` with identical literals, stale hint on test a, member "test b": test a's literal is rewritten.
  - Fix: when the name is enclosed in double backticks, judge the declaration from before the opening pair.

- [x] **An empty entry-point name hangs the patcher** (repro)
  - `src/DiffEngine/Inline/InlinePatcher.cs:915`: `IndexOf("", i, n)` returns `i` and `index += 0`, so `CallsOnLine` spins (or grows `matches` until out of memory) while holding the per-file mutex. `InlinePatchFile.TryParse` produces `["VerifyDocx", ""]` from a payload of `"VerifyDocx,"`.
  - Fix: skip null or empty (ideally any non-identifier) names in `EntryPoints()` (:78).

- [x] **"Copy received" and "Copy expected" turn tabs into four spaces** (repro)
  - `src/DiffEngineViewer/SelectionText.cs:119` (`All`) goes through `RowText.Flatten`. A whole-side copy has no columns to keep aligned. Pasting the result into a verified file changes it.
  - Fix: `.Select(_ => _.Text)`.

- [x] **Linux shortcuts follow physical key position rather than layout** (verified)
  - `native/src/deview.cpp:526` (`ReadKey`) tests raylib/GLFW key tokens, which are US key positions. On AZERTY the key labelled Q sends `KEY_A`, which accepts (with Shift, accepts all), and the key labelled A sends `KEY_Q`, which quits; Ctrl+A and Ctrl+Q are swapped. The macOS and WinForms heads follow the layout.
  - Fix: drain `GetKeyPressed()` and map letters through `GetKeyName(key)`, falling back to the position for non-Latin layouts. `IsKeyPressedRepeat` for navigation keys.

- [x] **InlineApplier's mutex is session scoped on macOS and Linux** (verified against .NET semantics)
  - `src/DiffEngine/Inline/InlineApplier.cs:84,355-365`: `DiffEngineInline_<sha>` has no prefix, so it is Local, which on Unix means per POSIX session, and every terminal is its own session. Rider's plugin and a viewer launched from a terminal test run do not exclude each other: both read, both swap, the later rename wins, and one literal is silently lost while both report Applied.
  - Fix: `Global\` prefix off Windows, falling back to Local on `UnauthorizedAccessException` or `IOException`.

- [x] **ProcessCleanup's process list is taken once and PIDs are not re-checked** (verified)
  - `src/DiffEngine/Process/ProcessCleanup.cs:27` (the only `Refresh` call, in the static constructor), `:94` (`TryGetProcessInfo`), `src/DiffEngine/Process/WindowsProcess.cs:155` (`TryTerminateProcess` kills whatever holds the PID).
  - A tool window from a previous run closed mid run: an AutoRefresh tool is reported `AlreadyRunningAndSupportsRefresh` and no window opens. If Windows has reused that PID, a passing test's `Kill`, or a replacement launch, terminates an unrelated process. Tools launched in this run never enter the list.
  - Fix: on a hit, re-read that PID's command line and drop it if it no longer matches; remove entries once terminated; add `(command, pid)` after a launch.

- [x] **Slow work runs inside SessionHost's lock, and the render loop takes that lock every frame** (verified)
  - `src/DiffEngineViewer/ViewerProgram.cs:321`: `host.Mutate(_ => Apply(...))` every frame, even with no input.
  - `src/DiffEngineViewer/Ipc/MessageHandler.cs:47-51`: `TrackedEntry.ForMove`/`ForDelete` (file reads, SHA-256 of images, the DiffPlex diff) is evaluated inside the `Mutate` lambda, contrary to the comment above it. `Act` (:102-133) runs `InlineApplier`, with its up to 10 s mutex wait, inside `Mutate`.
  - The window stalls, which `SessionHost`'s doc says lock-free reads exist to prevent.
  - Fix: build tracked entries before `Mutate`; skip the per-frame `Mutate` when the input is empty and the size unchanged; apply wire accepts outside the lock the way `AcceptAllRunner` does.

- [x] **`MessageHandler.Act` checks the conflict refusal outside the lock** (verified)
  - `src/DiffEngineViewer/Ipc/MessageHandler.cs:102-133` reads `host.State` twice, so `Queue[index]` can throw `ArgumentOutOfRangeException`, and the conflict check can pass just before a second framework's patch makes the entry conflicted, after which the accept picks a side.
  - Fix: do the lookup and the refusal inside the `Mutate` lambda.


## Bugs, unverified

Viewer model

- [ ] Removing the current entry can select one hidden inside a collapsed group: `Remove` keeps the old index without checking `VisibleEntries` (`src/DiffEngineViewer/ViewerSession.cs:1392`, also `Sync` at :183). `Toggle` (:1466-1480) already has the search; reuse it.
- [ ] A text selection survives its entry's text being replaced under the same key: `Describes` checks only key and variant (`src/DiffEngineViewer/TextSelection.cs:60-63`), so a re-run highlights and copies rows the reader never selected. Store a content token, such as the entry's `View(false)` instance, and compare by reference.
- [ ] The selection can move under an open entry menu: a wire `Focus` goes through `SelectKey` → `Select`, which does not clear `Menu` (`ViewerSession.cs:263-267`, `:1407-1425`), so Discard then acts on the newly selected entry. An arriving snapshot's `SelectKey` can likewise land between a painted frame and a key press. Clear `Menu` when the selection changes, or keep the entry's key in `MenuState`.
- [ ] "Accept all in <solution>" holds that solution's deletes because of an unrelated earlier failure: `InlineRefused` scans the whole queue (`ViewerSession.cs:629,1051,1129-1145`). Pass the group's own tally.
- [ ] Attached "Accept all in <solution>" posts one Accept per member, deletes included, with no held-delete rule (`src/DiffEngineViewer/ViewerProgram.cs:606-640`, `DispatchGroup`).
- [ ] The variant a reader picked is kept by index rather than identity, so a fold that drops or merges variants shows a different one (`ViewerSession.cs:1332`).
- [ ] A BMP with a height of `0x80000000` throws in `Math.Abs(int.MinValue)` (`src/DiffEngineViewer/Images/ImageHeader.cs:181`).
- [ ] `OwnerLink.ReadChanges` lacks `TrackedWatch`'s re-stamp guard, so a locked or unreadable file is re-read, re-diffed and replaced every 200 ms (`src/DiffEngineViewer/Ipc/OwnerLink.cs:244-279`).
- [ ] `TrackedWatch` can drop a pair re-staged under the same key between its stat and its `Refresh` (`src/DiffEngineViewer/TrackedWatch.cs:61-86`, `ViewerSession.cs:217-252`). Replace or drop only when the current entry is the same reference.

Windows head

- [ ] Selection highlight and hit-test drift: `MonoFont.Cell` rounds the 8.8 px advance to 9 while `DrawString` uses 8.8, so from about column 22 the highlight and the copy are a character off, two by column 66 (`src/DiffEngineViewer.Windows/MonoFont.cs:26-28`, `ViewerCanvas.cs:273-274`, `:484-495`). Use the unrounded advance.
- [ ] `ImageCache` never evicts, so every decoded picture stays for the life of a process the tray can keep hidden for days (`src/DiffEngineViewer.Windows/ImageCache.cs:13,45-58`).
- [ ] The initial window is 1100×700 physical pixels under PerMonitorV2 while the text scales with DPI; at 200% each pane shows a few characters (`ViewerForm.cs:115`). Size with `LogicalToDeviceUnits` and scale the pixel constants.
- [ ] Discrete input fields (`key`, `clickedButton`, `clickedQueueItem`, ...) overwrite each other within one `DoEvents` and are applied in a fixed order rather than the order they happened (`ViewerForm.cs:94-101`). Queue them.
- [ ] Right-clicking the row whose menu is already open leaves the popup and the model out of step: `Apply` returns before `ApplyMenu` when the screen is unchanged (`ViewerForm.cs:202-217`).
- [ ] Dragging the scrollbar thumb, or moving or resizing the window, runs a user32 modal loop inside `DoEvents`, so the panes freeze until release (`ViewerForm.cs:134,219-236`).
- [ ] Losing mouse capture mid drag (Alt+Tab, Win key, UAC) leaves `selecting`/`dragging` set (`ViewerCanvas.cs:534-664`). Override `OnMouseCaptureChanged`.
- [ ] At logoff the session can end before `PersistOwned` runs (`ViewerForm.cs:345-363`). Persist synchronously on `FormClosed` with `WindowsShutDown`.
- [ ] `ImageCache` reads with `FileShare.Read`, which can fail a concurrent accept's move or delete (`ImageCache.cs:68`), and it ignores `ImagePane.Hash`, so a same-size rewrite within timestamp granularity keeps the old picture.

Tray

- [ ] A failed bind on 3492 leaves `PiperServer.Start`'s faulted task unobserved: the tray runs without the listener while holding the mutex, then rethrows on exit (`src/DiffEngineTray/Program.cs:99,131-132`, `PiperServer.cs:31-32`).
- [ ] The locked-file kill uses a bare PID after an unbounded modal dialog; Restart Manager's `ProcessStartTime` is read and discarded (`Tracker.cs:556-578`, `FileLockKiller.cs:89-131`, `LockingProcess.cs`).
- [ ] `AddMove`'s update factory disposes `existing.Process` without nulling it, and `AddOrUpdate` factories can run twice under contention (`Tracker.cs:164-176`). A menu item built before the update then throws from "Open diff tool" on the UI thread.
- [ ] `PiperServer`'s handler runs synchronously on the accept loop until its first real await, and its `IOException` connection-reset catch can never match (`PiperServer.cs:52,63-76`). Use `Task.Run` and catch `SocketException`.
- [ ] `FileComparer` opens with `FileShare.Read`, which can fail a test's rewrite or delete of the received file during the 2 s scan; `count2` is ignored (`FileComparer.cs:10-41`).
- [ ] "Always kill locking processes" is ignored for accepts arriving over the socket (`Tracker.cs:596-600,955-958`).

Native

- [ ] F12 in the Linux viewer writes `screenshotNNN.png` into the working directory (raylib `SUPPORT_SCREEN_CAPTURE` default; the string is in the committed `.so`). `set(SUPPORT_SCREEN_CAPTURE OFF CACHE BOOL "" FORCE)` in `native/CMakeLists.txt`.
- [ ] Decoded image caches never evict on Linux (textures) or macOS (CGImages) (`native/src/deview.cpp:140,311-361`, `native/swift/Sources/Deview/Renderer.swift:60,436-452`).
- [ ] macOS drag-select clamps to the renderer's capacity rather than the rows drawn, so an overshooting drag copies up to three unseen rows (`ViewerView.swift:199-207`).
- [ ] Selection columns are UTF-16 units but the renderers treat them as glyph cells, so non-BMP characters shift the copy and can split a surrogate pair (`native/include/deview.h:61-69`, `SelectionText.cs`). Count in `Rune`s.
- [ ] `NativeResolver` still loads the glibc build on musl through the synthesized `linux-{arch}` candidate (`src/DiffEngineViewer/Native/NativeResolver.cs:77-108`).
- [ ] The Linux queue header ignores `pendingCount` (`deview.cpp:1002`).
- [ ] `deview_capture` flips scissor rectangles with the window height rather than the render target's (`deview.cpp:470`). Latent while every capture is 1100×700.
- [ ] Five-digit line numbers widen the Linux gutter by a cell, and hit-testing uses the first row's text start (`deview.cpp:643-657`).
- [ ] macOS has no autorelease pool around the hand-pumped frame (`Exports.swift:41-65`, `Runtime.swift`). Check with `OBJC_DEBUG_MISSING_POOLS=YES`.
- [ ] macOS Dock Quit and logout call `NSApp.terminate` directly, skipping `PersistOwned` (`Runtime.swift:65-104`).
- [ ] macOS App Nap can stall the loop while the window is covered (`Runtime.swift:244-249`).

Library and inline

- [ ] Settle-by-member counts queue entries, not call sites, so a passing sibling in the same member drops the one pending entry (`src/DiffEngine/Inline/InlineQueue.cs:235-239,304-331`; the same rule in `InlineStaging.cs:118-128`).
- [ ] The viewer launch (`UseShellExecute = false`, `src/DiffEngine/Viewer/ViewerLauncher.cs:95-102`) inherits the test host's std handles, so a redirected stdout pipe may keep `dotnet test` open until the viewer closes (the Verify#1229 problem). Confirm under MTP and VSTest.
- [ ] `MemberLine` picks the nearest same-named declaration even when it is below the hint: an F# local named like the test, or a same-named member in a nested type, moves the floor past the hint (`InlinePatcher.cs:971-1008`). Prefer the candidate whose span contains the hint.
- [ ] `NextMemberLine` compares indentation by character count, so a tab-indented body under a space-indented member ends the member early (`InlinePatcher.cs:855,867-868`).
- [ ] F#: a regular literal whose value looks like layout reads differently in the patcher (not stripped) and the test library (`SnapshotValue` strips), e.g. content `"\nx = \"\"\"\n"` is reported AlreadyApplied forever (`src/DiffEngine/Inline/FsStringLiteral.cs:39-46,81-84,123-146`).
- [ ] F#: escapes F# does not define (`"\d+"`) are kept literally by F# but rejected here as "not a string literal" (`FsStringLiteral.cs:158-203`).
- [ ] `WildcardFileFinder` enumerates from an unexpanded `%VAR%` root and throws `DirectoryNotFoundException` out of the `DiffTools` static constructor when a Program Files variable is undefined (`src/DiffEngine/WildcardFileFinder.cs:20-33`). Check `Directory.Exists` and catch IO errors.
- [ ] Sync `ViewerLaunchGate.Launch` blocks on the semaphore that async callers hold across awaits without `ConfigureAwait(false)`, so a bounded sync context (xUnit v2) can deadlock (`ViewerLaunchGate.cs:106,151-167`).
- [ ] `InlineApplier.Apply` can throw (mutex creation, a patcher bug) instead of returning `Failed`; in an owning viewer that unwinds the loop and skips `PersistOwned` (`InlineApplier.cs:84,152-164`).
- [ ] Test display names over about 220 characters push staged file names past 255 and are silently not persisted (`src/DiffEngine/Inline/InlineStaging.cs:373-394`). Truncate the test segment.
- [ ] Port 3493 is IANA-registered to Network UPS Tools; on a host running upsd, inline review silently degrades and every settle connects to it (`src/DiffEngine/Protocol/ViewerServer.cs:41-45`). At least trace a hint to set `DiffEngine_ViewerPort`.


## Perf

- [ ] **Queue projection is O(n²) per frame and per mutation under the lock** (`src/DiffEngineViewer/QueueProjection.cs`). `Order` calls `TestGroup` (two string allocations) for every pair and runs twice per inline change and per `Sync`; `Rows`/`Labels`/`Collisions` run every frame with n² comparisons, and grouped entries always collide so all four passes run. Compute the group key once per entry, group with a dictionary, detect collisions with a `(solution, label)` count, and cache `Rows` per queue instance.
- [ ] **An attached viewer polls `ListFull` five times a second, even while hidden.** The owner re-serialises every patch (base64 twice) inside its gate and the viewer re-parses all of it (`src/DiffEngineViewer/Ipc/OwnerLink.cs:100`, `src/DiffEngine/Protocol/ViewerListing.cs`, `src/DiffEngineTray/OwnedInlineHost.cs:259-284`). Add a generation or etag and answer "unchanged"; poll slower while hidden.
- [ ] **On macOS and Linux every passing verification builds a string of every process's command line**, even with logging off (`src/DiffEngine/Process/ProcessCleanup.cs:66-71`; the Unix `FindAll` ignores the name filter). Guard with `Logging.enabled`, and keep only commands that start with a resolved tool's exe path.
- [ ] `SelectionText.Summary` rebuilds the whole selected text every frame (`src/DiffEngineViewer/ScreenBuilder.cs:248`, `SelectionText.cs:131-142`), and a right-click builds both whole sides just to test for emptiness (`MenuState.cs:76`). Count from span lengths, once per selection.
- [ ] DiffPlex computes word-level sub-diffs that are never read, has no cost cap for large wholly-different files, and runs under the lock for inline changes and tracked arrivals (`src/DiffEngineViewer/DiffRows.cs:17-22`, `QueueEntry.cs:59-63`). Use a whole-line chunker for the word pass; guard with an edit-distance lower bound.
- [ ] macOS repaints the whole window every frame (`native/swift/Sources/Deview/Runtime.swift:139-140`). Redraw only when the frame, bounds or a picture stamp change, and cache scaled pictures.
- [ ] Windows `Thread.Sleep(16)` sleeps about 30 ms at the default timer resolution, and a hidden process wakes about 34 times a second forever (`src/DiffEngineViewer.Windows/FormsViewerWindow.cs:65`). Use `MsgWaitForMultipleObjectsEx`, with a longer timeout while hidden.
- [ ] Windows image panes rescale from full resolution and redraw the checkerboard on every paint (11 to 40 ms per image), and decode on the UI thread (`ViewerCanvas.cs:385-420`, `ImageCache.cs:56-71`). Cache the composited scaled bitmap per path, stamp and size.
- [ ] All three heads lay out each row's full text though only about 35 cells fit (`ViewerCanvas.cs:503-508`, `src/DiffEngineViewer/Native/ScreenPayload.cs:164-177`); a 1 MB minified line costs about 0.8 s per paint on Windows. Truncate to the visible columns before drawing or marshalling.
- [ ] raylib busy-waits the last 5% of every frame: `set(SUPPORT_PARTIALBUSY_WAIT_LOOP OFF CACHE BOOL "" FORCE)` in `native/CMakeLists.txt`.
- [ ] `InlineStaging.Clear` walks the `obj` tree and re-reads and parses every staged `.inlinepatch` on each verification (`src/DiffEngine/Inline/InlineStaging.cs:94-192`). Cache per directory keyed on `LastWriteTimeUtc`.
- [ ] Tray: `SafeMove`'s 8 × 400 ms retry runs on the UI thread even for failures that cannot clear, such as a read-only target or a missing directory (`src/DiffEngineTray/Tracker.cs:535-585`, `FileEx.cs:73-90`). Retry only sharing violations.
- [ ] Tray: the 2 s scan re-reads every equal-size, different pair from scratch (`Tracker.cs:81-97`, `FileComparer.cs:18-57`). Cache length and write time with the last result.
- [ ] `PiperClient` has no unowned-port memory for 3492, and `TrayAvailable` is cached at type init, so after the tray exits every send pays a refused connect (`src/DiffEngine/Tray/PiperClient.cs:138-153`, `PendingFiles.cs:47-49`).


## Appendix: repro tests

Each test fails on 4244ebe6. `EmptyEntryPointDoesNotHang` leaves a spinning thread-pool thread behind when it fails.

`src/DiffEngine.Tests/ReviewReproTests.cs`:

```cs
public class ReviewReproTests
{
    static PatchStatus Cs(string source, int hint, InlinePatchMode mode, string? expression, string content, out string newSource, string? member = null) =>
        InlinePatcher.TryApply(SourceLanguage.CSharp, source, hint, mode, expression, null, member, null, false, content, out newSource, out _);

    const string twoDups = "class Tests\n{\n    async Task Test()\n    {\n        await Verify(a).Snapshot(\"dup\");\n        await Verify(b).Snapshot(\"dup\");\n    }\n}\n";

    [Test]
    public async Task ReapplyingASetDoesNotRewriteTheSibling()
    {
        var first = Cs(twoDups, 6, InlinePatchMode.Set, "\"dup\"", "new", out var once, "Test");
        await Assert.That(first).IsEqualTo(PatchStatus.Applied);
        await Assert.That(once).Contains("Verify(a).Snapshot(\"dup\")");
        await Assert.That(once).Contains("Verify(b).Snapshot(\"new\")");

        var second = Cs(once, 6, InlinePatchMode.Set, "\"dup\"", "new", out var twice, "Test");
        await Assert.That(twice).Contains("Verify(a).Snapshot(\"dup\")");
        await Assert.That(second).IsEqualTo(PatchStatus.AlreadyApplied);
    }

    [Test]
    public async Task ReapplyingARemoveDoesNotRemoveTheSibling()
    {
        var first = Cs(twoDups, 6, InlinePatchMode.Remove, "\"dup\"", "", out var once, "Test");
        await Assert.That(first).IsEqualTo(PatchStatus.Applied);

        Cs(once, 6, InlinePatchMode.Remove, "\"dup\"", "", out var twice, "Test");
        await Assert.That(twice).Contains("Verify(a).Snapshot(\"dup\")");
    }

    [Test]
    public async Task FsBacktickMemberNameBoundsTheSearch()
    {
        var source = SourceLanguage.NormalizeNewlines("module Tests\n\nlet ``test a`` () =\n    Verifier.Verify(a).Snapshot(\"dup\").ToTask()\n\nlet ``test b`` () =\n    Verifier.Verify(b).Snapshot(\"dup\").ToTask()\n");
        var status = InlinePatcher.TryApply(SourceLanguage.FSharp, source, 4, InlinePatchMode.Set, null, "dup", "test b", null, false, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Verify(a).Snapshot(\"dup\")");
        await Assert.That(newSource).Contains("Verify(b).Snapshot(\"new\")");
    }

    [Test]
    public async Task EmptyEntryPointDoesNotHang()
    {
        var source = "class Tests\n{\n    Task Test() =>\n        Verify(a);\n}\n";
        var task = Task.Run(() => InlinePatcher.TryApply(SourceLanguage.CSharp, source, 4, InlinePatchMode.Append, null, null, null, ["VerifyDocx", ""], true, "x", out _, out _));
        var finished = await Task.WhenAny(task, Task.Delay(5000)) == task;
        await Assert.That(finished).IsTrue();
    }
}
```

`src/DiffEngineViewer.Tests/ReviewReproViewerTests.cs`:

```cs
public class ReviewReproViewerTests
{
    [Test]
    public async Task AnUnchangedListingKeepsTheMenuOpen()
    {
        var state = Fixtures.Attached(Fixtures.Pending(Fixtures.Patch()));
        var open = ViewerSession.OpenMenu(state, 0);
        await Assert.That(open.Menu).IsNotNull();

        // The next poll, 200ms later, with the owner reporting exactly the same queue
        var synced = ViewerSession.Sync(open, Fixtures.Pending(Fixtures.Patch()), [], null);
        await Assert.That(synced.Menu).IsNotNull();
    }

    [Test]
    public async Task AnArrivalAfterTheQueueEmptiedClearsExit()
    {
        var state = Fixtures.Inline(Fixtures.Patch());
        var settled = ViewerSession.Settle(state, state.Queue[0].Key);
        await Assert.That(settled.Exit).IsTrue();

        var arrived = ViewerSession.EnqueueInline(settled, Fixtures.Patch("OtherTests.cs", 7));
        await Assert.That(arrived.Queue.Count).IsEqualTo(1);
        await Assert.That(arrived.Exit).IsFalse();
    }

    [Test]
    public async Task CopyingAWholeSideKeepsTabs()
    {
        var entry = Fixtures.Move(left: "a\tb", right: "a\tb");
        await Assert.That(SelectionText.All(entry, PaneSide.Left)).IsEqualTo("a\tb");
    }
}
```
