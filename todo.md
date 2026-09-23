# Review todo

Findings from a review of `main` at 4244ebe6 (2026-09-23). A second pass checked every item left unverified, on c37bf9e1 (see "Bugs, second pass").

- **repro**: a test fails on the commit the item was checked on: the appendix for the first pass, the local branch `review-repros` for the second.
- **verified**: confirmed by reading the code (or running the API involved), no test yet.
- **cannot verify here**: needs a platform this machine lacks; the item says what would settle it.


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

- [x] **Committed Linux native binaries need glibc 2.38** (verified)
  - **Fixed:** the Linux jobs build inside `quay.io/pypa/manylinux_2_28_$(uname -m)` via `native/build-linux.sh`, and a "Check glibc floor" step fails the job if `objdump -T` shows anything above `GLIBC_2.28`. The binaries rebuilt that way (#882) need at most `GLIBC_2.27` and link `libGL.so.1` rather than `libOpenGL.so.0` (`readelf -V`/`-d` in WSL).
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

- [ ] **An attached "Accept all in <solution>" deletes verified files whose snapshots were not written** (repro, second pass)
  - `DispatchGroup` (`src/DiffEngineViewer/ViewerProgram.cs:663-697`) posts one `Accept` per member, deletes included (`:683`), before any answer comes back, and the owner carries each out as a single accept with no held-delete rule: the tray through `OwnedInlineHost.cs:302-304` and `Tracker.cs:956` to `File.Delete` at `:1061`, a viewer through `MessageHandler.cs:156`. #878 fixed the tray's accept-all and the AcceptAll verb, not this path. The owning viewer's own group accept does hold deletes (`ViewerSession.cs:664-675`).
  - Test (`review-repros`): `An_attached_accept_all_in_a_solution_holds_its_deletes_when_a_snapshot_was_not_written`. The patch comes back NotFound and the verified file is deleted anyway, so no copy of the snapshot is left. Its owner is a viewer; the tray owner's delete path was read, not run.
  - Fix: the client cannot know the patch outcomes when it posts, so the rule belongs with the owner, for example a group verb that carries the member keys through the owner's batch and its hold rule.


## Bugs

- [x] **The Linux viewer never showed a frame or read input** (repro, found while verifying the items below)
  - **Fixed:** `native/CMakeLists.txt` switches `SUPPORT_CUSTOM_FRAME_CONTROL` and `SUPPORT_BUSY_WAIT_LOOP` back off, with every other flag raylib's `config.h` defaults to 0. Test: `PixelTests.PresentWaitsForTheNextFrame`, which the Ubuntu job runs under xvfb against the renderer built from source.
  - raylib 6.0's `cmake/ParseConfigHeader.cmake` turns every `#define SUPPORT_X <value>` in `config.h` into an option defaulting to ON, whatever the value (raysan5/raylib#5844 fixed it after 6.0), and `CUSTOMIZE_BUILD ON` skips `config.h`'s own values. The Linux configure output listed `SUPPORT_CUSTOM_FRAME_CONTROL=ON`, `SUPPORT_BUSY_WAIT_LOOP=ON` and every image format raylib has (build-native run 35812014703).
  - With custom frame control, `EndDrawing` skips `SwapScreenBuffer`, the `SetTargetFPS` wait and `PollInputEvents`, the only callers of `glfwSwapBuffers` and `glfwPollEvents`, and `deview_present` calls none of them itself. Disassembly of both committed `.so` files shows neither has a call site. So the window never painted and ignored keys, mouse and its close button, while the managed loop, which relies on the present to pace it, spun; and it owned 3493 the whole time, accepting snapshots it could not show.
  - Measured on the committed linux-x64 `.so` in WSL (python ctypes, hidden window): a bare `deview_present` loop ran at 41,659 fps across 11 cores.
  - Every committed Linux binary since the first (#733) was built this way. The pixel snapshots never noticed, because `deview_capture` draws into a texture and never calls `EndDrawing`.

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


## Bugs, second pass

Every item that was unverified, checked on c37bf9e1, after #878, #881 and #884. All of them hold, and none of the three fixed any. The repro tests are on the local branch `review-repros`, one class per area: `ReviewReproSessionTests` and `ReviewReproWatchTests` (viewer model), `ReviewReproWindowsTests`, `ReviewReproTrayTests`, `ReviewReproPatcherTests`, `ReviewReproLibraryTests`. Each test fails on c37bf9e1 except a control (`ControlSpaceIndentedLocalLeavesTheSiblingAlone`) and a measurement (`HowLongADecodeHoldsTheFile`). The attached "Accept all in <solution>" item moved to Data loss.

Viewer model

- [ ] **Removing the current entry can select one hidden inside a collapsed group** (repro)
  - `Remove` (`src/DiffEngineViewer/ViewerSession.cs:1449`) and `Sync` (`:186`) keep the old index and never check `VisibleEntries`, so after an accept, discard, settle or refresh, the panes and Accept act on an entry under a fold that nothing in the list highlights. An attached viewer gets there through `Sync` on every accept.
  - Tests: `Accepting_the_entry_being_read_does_not_select_one_under_a_fold`, `A_listing_without_the_entry_being_read_does_not_select_one_under_a_fold`.
  - Fix as suggested: run `Toggle`'s search (`:1530-1544`) after `Clamp` in both. Tried: both pass and nothing else breaks.

- [ ] **A text selection survives its entry's text being replaced under the same key** (repro)
  - `Describes` (`src/DiffEngineViewer/TextSelection.cs:60-63`) compares key and variant only. A re-run rebuilds the entry (`ViewerSession.cs:53-73`) without touching `Selection`, so the highlight and the copy point into the new rows.
  - Test: `A_rerun_that_replaces_the_text_under_a_selection_ends_the_selection` (copies "added two" where "brown dog" was selected).
  - Fix as suggested: the `View(false)` reference works, since a `with` keeps it and a rebuild does not. It also ends the selection when a conflicted entry gains a variant, which errs the safe way.

- [ ] **An open entry menu acts on whatever is selected when it is clicked, and the selection can move under it** (repro)
  - Menu items act on `state.Current` (`src/DiffEngineViewer/ViewerProgram.cs:445`, discard at `ViewerSession.cs:1263`, the attached key at `ViewerProgram.cs:586`), and `SelectKey`/`Select` (`ViewerSession.cs:309`, `:1488`) never clear `Menu`.
  - Triggers: a wire Focus from the tray menu or an IDE (`MessageHandler.cs:191`); and, attached, with nobody touching anything, a test re-sending an identical patch. The tray folds it into the same entry and stashes a Focus (`OwnedInlineHost.cs:198`) that its next listing carries (`:274`); `Sync` keeps the menu because the entries are unchanged, then `OwnerLink.cs:128` selects.
  - Tests: `A_wire_focus_does_not_retarget_an_open_entry_menu`, `A_focus_riding_the_owners_listing_does_not_retarget_an_open_entry_menu`.
  - Fix: `Menu = null` in `Select`, the only path that moves the selection without changing the queue. Not covered: an arrival landing between a painted frame and a key press, which focusing arrivals are built on.

- [ ] **"Accept all in <solution>" holds that solution's deletes because of an unrelated earlier failure** (repro)
  - `AcceptGroup` passes `refused: notWritten > 0` (`ViewerSession.cs:675`) and `SweepTracked` ORs in `InlineRefused` (`:1097`), which scans every inline entry in the queue (`:1182-1188`), not the group's.
  - Test: `An_earlier_failure_in_another_solution_does_not_hold_this_solutions_deletes`.
  - The suggested fix is incomplete: the flag must be `notWritten + failed > 0`, because members that failed stay queued and only the scan catches them today. `InlineRefused` then has no non-discarding caller left.

- [ ] **The variant a reader picked is kept by index rather than identity** (repro)
  - `Project` rebuilds a changed entry with `QueueEntry.ForInline(pending, entry.SelectedVariant)` (`ViewerSession.cs:1378`), which only clamps (`QueueEntry.cs:98`). An origin settle (`ViewerSession.cs:125-132`) or a re-run merging two variants (`InlineQueue.cs:137-182`) moves the reader to another framework's content with nothing to say so, and Accept then applies that one (`ViewerSession.cs:580-586`).
  - Tests: `ASettleOfAnEarlierVariantKeepsTheVariantOnScreen`, `AReRunThatMergesAnEarlierVariantKeepsTheVariantOnScreen`.
  - Fix: keep the variant that holds one of the old variant's origins, and fall back to the index only when none does.

- [ ] **A BMP with a height of `0x80000000` throws in `Math.Abs(int.MinValue)`** (repro)
  - `src/DiffEngineViewer/Images/ImageHeader.cs:181`. `FileSide.Read`'s catch-all (`FileSide.cs:41-44`) turns the `OverflowException` into an unreadable side warning "Negating the minimum value of a twos complement number is invalid", with no stamp, so it is re-read every 200 ms (and, attached, closes an open menu each time; see `ReadChanges` below). No crash.
  - Tests: `ABmpWithTheMinimumHeightIsStillABmp`, `ABmpWithTheMinimumHeightIsNotReportedUnreadable`.

- [ ] **`OwnerLink.ReadChanges` replaces an unreadable file's entry on every pump, which closes an attached viewer's menu within 200 ms** (repro)
  - Both it (`src/DiffEngineViewer/Ipc/OwnerLink.cs:244-279`) and `TrackedWatch` (`TrackedWatch.cs:101-117`) compare the stored stamp, null after a failed read, with a fresh stat, so both re-read and re-diff a locked file five times a second (the diff runs in `QueueEntry.cs:59-63`). Only `TrackedWatch` then drops the rebuilt entry (`Changed`, `:147-156`). `ReadChanges` hands `Sync` a new reference every pump, which defeats #881's `SameEntries` guard.
  - Test: `AnUnreadableTrackedFileLeavesAnAttachedViewersMenuOpen`.
  - The suggested fix is incomplete: porting the guard stops the menu closing but not the re-reads in either path. Storing the stat's stamp on a failed read would leave a briefly locked file unreadable until it changed. A retry backoff would do both.

- [ ] **`TrackedWatch` can drop a pair re-staged under the same key between its stat and its `Refresh`** (repro)
  - `Pump` reads the queue without the lock (`src/DiffEngineViewer/TrackedWatch.cs:65`), stats the files outside it, and applies `Refresh` under `Mutate` (`:85`), which drops or replaces by key alone (`ViewerSession.cs:256-269`). A re-run that deletes then rewrites its received file lands in that gap, and in an owning viewer the pair is gone until the test fails again.
  - Tests: `APassDoesNotDropAPairReStagedAfterItsStat` (6 of 6 runs), `ARefreshDoesNotDropAnEntryThatArrivedAfterTheStat`.
  - Fix as suggested: act only when the current entry is the reference the stat saw.

- [ ] **Selection columns are UTF-16 units, but every head reports cells** (repro for the copy, verified for the heads)
  - All three heads turn the pointer's x into cells (`src/DiffEngineViewer.Windows/ViewerCanvas.cs:273-274`, `native/src/deview.cpp:708-722`, `native/swift/Sources/Deview/ViewerView.swift:212-219`), and `SelectionText` indexes UTF-16 with them (`:63-83`, `:93-113`). GDI+ draws a non-BMP code point as exactly one cell, and ImGui lays out one glyph per code point.
  - Tests: `ADragAcrossOneNonBmpCharacterCopiesAllOfIt` (copies a lone high surrogate), `ADragAfterANonBmpCharacterCopiesWhatWasHighlighted` (copies a low surrogate and "a" for "ab").
  - Counting in Runes is incomplete: CJK falls back to a font 1.83 cells wide on Windows, a combining mark takes none, and Core Text substitutes fonts with their own widths. Either put every code point on the grid or have each head report string indexes from its own layout; at the least, snap the ends to Rune boundaries. `Summary` and select-all's end column (`ViewerSession.cs:474`) count UTF-16 too.

Windows head (measured in the test host at 96 DPI; this machine is 120)

- [ ] **Selection highlight and hit-test drift from the glyphs** (repro at 100%, 150%, 175% and 200%; not at 125%)
  - `Graphics.DrawString` with `GenericTypographic` places glyphs at the unhinted advance (8.798 px at 96 DPI), while `MonoFont.Cell` rounds it (9, `src/DiffEngineViewer.Windows/MonoFont.cs:26-28`), and the highlight (`ViewerCanvas.cs:486-494`) and `ColumnAt` (`:273-274`) multiply by the rounded width. Drift at column 66: −13.2 px at 96 DPI, +13.2 at 144, +26.2 at 168, −26.8 at 192.
  - Tests: `CellAgainstTheDrawnAdvance`, `HighlightAtColumn66CoversItsGlyph` (the bar at column 66 hit-tests as 65).
  - Fix as suggested, the subtitle width at `:430` included. The integer cell can stay for layout.

- [ ] **`ImageCache` never evicts** (repro)
  - Keyed by path and only replaced for the path asked about (`src/DiffEngineViewer.Windows/ImageCache.cs:45-58`); `ViewerCanvas.Draw` never prunes it. The process lives while the queue has anything in it, hidden or not.
  - Test: `EveryPictureEverDrawnStaysDecoded` (ten 400×300 pairs, each accepted: 20 pictures and 9.4 MB of unmanaged GDI+ memory held, on a screen showing none).
  - Fix: keep only the paths on the current screen.

- [ ] **The initial window is 1100×700 device pixels under PerMonitorV2** (repro)
  - `ViewerForm.cs:115`, with `AutoScaleMode` left at Inherit, which scales nothing on a top-level form, while the 11 pt font grows with DPI. Characters a pane with a queue showing: 34 at 96 DPI, 23 at 120, 15 at 144, 4 at 192.
  - Test: `WhatTheDefaultWindowHoldsAtEachScale`.
  - Fix: `LogicalToDeviceUnits`, clamped to the working area, since 1100×700 at 150% is too tall for 1080p. Of the pixel constants only `grab = 4` matters.

- [ ] **Discrete input overwrites itself within one `DoEvents` and is applied in a fixed order** (repro)
  - One slot per kind of input (`ViewerForm.cs:94-101`), one `ViewerInput` per `Drain` (`:304-337`), applied as the click chain, then buttons, then the key (`ViewerProgram.cs:440-524`). It needs a slow frame, for example the loop waiting in `host.Mutate` behind an accept on the mutex.
  - Tests: `TwoKeysInOnePumpAreTwoCommands` (two Downs scroll once), `AKeyThenAClickInOnePumpAreAppliedInTheirOrder`: d pressed on one entry, then a click on another, discards the clicked one, which the reader never looked at. With a, it would be accepted into source.
  - Fix: queue them and emit one per `Drain`, without waiting while more are queued. That also fixes the next item.

- [ ] **Right-clicking the row whose menu is open leaves the popup and the model out of step** (repro)
  - The menu filter closes the popup (`ViewerForm.cs:144-156`), the model keeps an equal menu (`ViewerProgram.cs:496-500`), and `Apply` returns before `ApplyMenu` (`ViewerForm.cs:206-209`), so right-clicking that row does nothing until something else changes the screen.
  - Test: `RightClickingTheRowWhoseMenuIsOpen`. Fix: call `ApplyMenu` before the early return.

- [ ] **Dragging the scrollbar thumb, or moving or resizing the window, freezes the panes** (repro for the thumb)
  - `Present` returns only after `DoEvents` (`src/DiffEngineViewer.Windows/FormsViewerWindow.cs:58-60`), and the thumb is tracked in the scroll bar's own modal loop. Move and resize rest on `WM_ENTERSIZEMOVE`'s documentation. The class comment's "no nested message loops" (`:7-9`) is wrong.
  - Test: `DraggingTheThumbHoldsThePump` (one `DoEvents` took 560 ms; the panes jumped on release).
  - A fix needs a way to run a frame from inside the modal loop, which `IViewerWindow` has no hook for.

- [ ] **Losing mouse capture mid drag leaves `selecting`/`dragging` set** (repro)
  - Only `OnMouseUp` clears them (`ViewerCanvas.cs:649-665`), and a window without capture never hears the button come up.
  - Tests: `LosingCaptureMidDragEndsTheDrag` (the selection follows the pointer with no button held), `LosingCaptureMidSplitterDragEndsTheDrag`.
  - Fix: `OnMouseCaptureChanged` acting when `!Capture`, and ending the drag when a move arrives without the left button.

- [ ] **At logoff the session can end before `PersistOwned` runs** (verified)
  - WinForms closes and disposes the form inside `WM_ENDSESSION`. The loop notices only after `DoEvents` returns, and `Run`'s finally then joins the listener and the watcher, up to 2 s each, before persisting (`ViewerProgram.cs:256-281`). The documentation says the session may end once every application has returned from that message.
  - Test: `EndSessionReturnsBeforeTheLoopHasStartedToPersist` (`WM_ENDSESSION` returned at 330 ms, before `Run`'s finally had started). Only a real logoff shows whether Windows ends the process in that gap.
  - The suggested fix is incomplete: the form has no route to the session. It must set `Closing` before persisting, so late arrivals are refused and staged by their senders, and must not wait on the runner or the joins.

- [ ] **`ImageCache` ignores `ImagePane.Hash`, and reads with `FileShare.Read`** (repro)
  - The key is the path, checked by write time and length (`ImageCache.cs:27-51`), and `DrawImage` never passes the hash (`ViewerCanvas.cs:351`). In the app, that takes a pair re-sent within the timestamp granularity.
  - Test: `ARewriteWithTheSameStampKeepsTheOldPicture` (180 of 199 back-to-back writes here kept the stamp). Fix: pass the hash into `Get`.
  - The file is held only for the `ReadAllBytes`: 0.1 ms at 100 KB, 2.8 ms at 10 MB (`HowLongADecodeHoldsTheFile`). The viewer's accept-all worker can overlap it and does not retry; the tray's move retries, its discard does not. `FileSide.Read` opens the same files the same way (`FileSide.cs:36, 39`), so widen the sharing there too.

Tray

- [ ] **A failed bind on 3492 leaves the tray running without its listener** (verified)
  - `PiperServer.Start` is async, so a bind failure (`src/DiffEngineTray/PiperServer.cs:31-32`) faults the returned task, which `Program` awaits only after `Application.Run` returns (`Program.cs:99`, `:132`), holding the "DiffEngine" mutex throughout; on exit it is logged as Fatal "Failed at startup" and rethrown. Meanwhile, if another process holds 3492, `PortIsHeld` says yes and every move and delete goes to it; a bind that failed otherwise sends moves to 3493 without exe, arguments or process id.
  - Needs the named mutex, which the real tray holds, so not unit tested.
  - Fix: bind synchronously, as `ViewerServer.TryBind` does, and warn; or check `IsFaulted` before `Application.Run`.

- [ ] **The locked-file kill uses a bare PID after an unbounded modal dialog** (verified)
  - `FileLockKiller.cs:89-100` drops `RM_UNIQUE_PROCESS.ProcessStartTime`, and `LockingProcess` has nowhere to keep it. After `form.ShowDialog()` (`LockedFilesHandler.cs:12-13`), which has no timeout, `Kill` opens each process by id (`FileLockKiller.cs:121`). Only the dialog path has a long window; PID reuse cannot be forced in a test.
  - Fix: keep the start time and compare it with the opened process's through the same handle before killing.

- [ ] **"Open diff tool" from a menu built before a re-run's move throws on the UI thread** (repro)
  - `AddMove`'s update factory disposes `existing.Process` (`src/DiffEngineTray/Tracker.cs:207`) and leaves it on the old move, which a menu that was open across the re-run still holds (`MenuBuilder.cs:284`; the menu is rebuilt on each Opening). Nothing handles `ThreadException`, so the user gets WinForms' unhandled exception dialog. The factory running twice under contention only leaks a handle.
  - Test: `OpenDiffToolFromAMenuBuiltBeforeTheMoveWasUpdated`.
  - The suggested fix is incomplete: nulling it stops the throw, but "Open diff tool" would then attach the new tool to the orphaned move, which nothing kills. Look the move up by key when clicked, and dispose outside the factory.

- [ ] **A connection that resets while waiting to be accepted shows the "open an issue" box and stalls the listener** (repro)
  - `AcceptTcpClientAsync` throws a bare `SocketException` ConnectionReset (measured on .NET 10), but the accept loop's catch expects an `IOException` wrapping one (`PiperServer.cs:63-67`), so it reaches `ExceptionHandler.Handle` and its modal box, on the accept loop's own thread. `Handle`'s catch (`:126-130`) is right as it is: a reset during the read is the wrapped form.
  - Test: `AClientThatResetsBeforeItIsAcceptedIsNotReportedAsAnError`.
  - Fix: catch `SocketException` in the accept loop only. The handler running inline until its first real await is true, but only widens the window. Also, `PiperTest.ClientDisconnectsAbruptly` never sends a reset (`TcpClient.Dispose` closes cleanly); `Socket.Close(0)` does.

- [ ] **`FileComparer` blocks a test's delete of its received file during a compare** (repro)
  - Both files are opened with `FileShare.Read` (`src/DiffEngineTray/FileComparer.cs:10-16`) for the whole compare, which since #884 runs once per change of a same-size pair.
  - Test: `ATestCanDeleteItsReceivedFileWhileTheScanComparesIt` (a 64 MB pair).
  - `count2` cannot give a wrong answer today, because `FileShare.Read` keeps writers out. So widening the sharing alone is wrong: truncating the received file mid compare then made `FilesAreEqual` return true against a 64 MB verified file, and the scan would drop the move and kill its tool. Widen it only together with returning false when `count1 != count2`.

- [ ] **"Always kill locking processes" is ignored for accepts arriving over the socket** (repro)
  - Wire accepts go through `AcceptWithoutPrompting` (`Tracker.cs:1073-1090`), and `ShouldKill` returns false at `:720-724`, before the resolver, the only place that reads the setting (`LockedFilesHandler.cs:7-10`). The viewer is told to accept from the tray menu, where the same accept kills without asking.
  - Test: `AlwaysKillAppliesToAnAcceptArrivingOverTheSocket`.
  - Fix: read the setting in `ShouldKill` before the `NeverPrompt` branch. `ALockedMoveIsRefusedWithoutPrompting` requires that the resolver, which builds a dialog, is never consulted.

Native (the Linux items were unreachable until "The Linux viewer never showed a frame" was fixed; these are verdicts on the code as it behaves from there)

- [x] F12 in the Linux viewer writes `screenshotNNN.png` into the working directory (raylib `SUPPORT_SCREEN_CAPTURE` default; the string is in the committed `.so`). `set(SUPPORT_SCREEN_CAPTURE OFF CACHE BOOL "" FORCE)` in `native/CMakeLists.txt`.
  - **Fixed** with the frame control above (verified: `EndDrawing` takes `IsKeyPressed(KEY_F12)` to `TakeScreenshot` in the working directory). It could not fire while nothing polled input, and could as soon as something did.

- [ ] **Decoded image caches never evict on Linux or macOS** (verified)
  - `state.pictures` (`native/src/deview.cpp:140`) drops an entry only when that path is asked for again and has changed or gone (`:318-344`), or at shutdown; `Renderer.swift:60` likewise (`:436-452`). Bounded by one viewer session, which ends when the queue empties.
  - Fix: after each frame, drop what the frame did not use.

- [ ] **macOS drag-select clamps to the renderer's capacity rather than the rows drawn** (verified)
  - `draggedRow` clamps to `capacity - 1` (`native/swift/Sources/Deview/ViewerView.swift:199-207`) while the managed side draws `Rows - 8` (`ScreenBuilder.cs:10-13`), and neither `DiffView.Unfold` nor `SelectionText.Clamp` clamps to the rows shown. An overshooting drag highlights to the last visible row, but the status line counts, and Cmd+C copies, up to three or four rows below it.
  - Fix: clamp to the drawn rows, as Linux's `RowAt` does (`deview.cpp:693-703`).

- [ ] **The Linux queue header ignores `pendingCount`** (verified)
  - `deview.cpp:1014` draws the literal "Pending". macOS, WinForms and the text renderer draw "Pending (N)", which ABI 7 added the field for.

- [ ] **`deview_capture` flips scissor rectangles with the window height** (verified, latent)
  - `RenderDrawData` uses `GetScreenHeight()` (`deview.cpp:470`, `:484-489`), and `BeginTextureMode` changes only the render target's height. Every capture is 1100×700 in a 1100×700 window, so no output is wrong today.
  - Fix: `drawData->DisplaySize.y`.

- [ ] **Five-digit line numbers widen the Linux gutter, and hit-testing uses the first row's text start** (verified)
  - `"%c %4d"` (`deview.cpp:655`) is seven cells from line 10000, and `textLeft` is read from the first row drawn (`:665-669`) and used for every row. Only files over 9999 lines; macOS uses a fixed eight-cell gutter.

- [ ] **macOS has no autorelease pool around the hand-pumped frame** (verified; the leak rate needs a Mac)
  - Nothing pushes a pool in `deview_present` or `deview_poll_input` (`native/swift/Sources/Deview/Exports.swift:41-65`, `Runtime.swift:131-150, 244-249`), and the dylib imports neither `objc_autoreleasePoolPush` nor `Pop`. objc4 then creates a pool that drains only when the thread exits. Check with `OBJC_DEBUG_MISSING_POOLS=YES`.
  - Fix: `autoreleasepool {}` around each export's body, as GLFW does.

- [ ] **macOS Dock Quit and logout skip `PersistOwned`** (verified)
  - There is no app delegate and no `applicationShouldTerminate` (`Runtime.swift:92-96`). A quit Apple event becomes `terminate:`, after which cleanup in `main` never runs (Apple's `terminate(_:)` documentation), so #878's `finally` does not either. macOS has no tray, so an owning viewer's queue is lost.
  - Fix, as GLFW does: an app delegate that records the quit and returns `.terminateCancel`. `.terminateLater` would deadlock, running a modal loop inside the pump while the managed thread waits.

- [ ] **macOS App Nap can stall the loop while the window is covered** (cannot verify here)
  - Nothing opts out (no `beginActivity`, `NSAppSleepDisabled` or power assertion), the only wait is `nextEvent(until: now + 1/60)` (`Runtime.swift:244-249`), and a Focus queued by an arriving patch waits for the next managed frame.
  - Check on a Mac: cover the viewer for a minute, confirm Activity Monitor shows App Nap, then time how long a failing inline test takes to bring it forward against an uncovered window.

- [ ] **`NativeResolver` loads the glibc build on musl through the `linux-{arch}` candidate** (cannot verify here)
  - For `linux-musl-x64` the order is `runtimes/linux-musl-x64` (not shipped), then `runtimes/linux-x64` (glibc), then beside the exe (`src/DiffEngineViewer/Native/NativeResolver.cs:65-108`), against its own comment (`:79-80`). #788 fixed the same thing in `BundledViewerDirectory` but not here.
  - A failed load is harmless: it is caught and the queue is staged before exit 4. All 238 undefined symbols are names musl exports, though, so the load could succeed, and a crash after it would skip staging.
  - Check on Alpine x64 with mesa-gl, libx11, libxext, libsm, libice and libstdc++: whether the tool renders or crashes.

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

- [x] **Queue projection is O(n²) per frame and per mutation under the lock** (`src/DiffEngineViewer/QueueProjection.cs`). `Order` calls `TestGroup` (two string allocations) for every pair and runs twice per inline change and per `Sync`; `Rows`/`Labels`/`Collisions` run every frame with n² comparisons, and grouped entries always collide so all four passes run. Compute the group key once per entry, group with a dictionary, detect collisions with a `(solution, label)` count, and cache `Rows` per queue instance.
- [ ] **An attached viewer polls `ListFull` five times a second, even while hidden.** The owner re-serialises every patch (base64 twice) inside its gate and the viewer re-parses all of it (`src/DiffEngineViewer/Ipc/OwnerLink.cs:100`, `src/DiffEngine/Protocol/ViewerListing.cs`, `src/DiffEngineTray/OwnedInlineHost.cs:259-284`). Add a generation or etag and answer "unchanged"; poll slower while hidden.
- [x] **On macOS and Linux every passing verification builds a string of every process's command line**, even with logging off (`src/DiffEngine/Process/ProcessCleanup.cs:66-71`; the Unix `FindAll` ignores the name filter). Guard with `Logging.enabled`, and keep only commands that start with a resolved tool's exe path.
- [x] `SelectionText.Summary` rebuilds the whole selected text every frame (`src/DiffEngineViewer/ScreenBuilder.cs:248`, `SelectionText.cs:131-142`), and a right-click builds both whole sides just to test for emptiness (`MenuState.cs:76`). Count from span lengths, once per selection.
- [x] DiffPlex computes word-level sub-diffs that are never read, has no cost cap for large wholly-different files, and runs under the lock for inline changes and tracked arrivals (`src/DiffEngineViewer/DiffRows.cs:17-22`, `QueueEntry.cs:59-63`). Use a whole-line chunker for the word pass; guard with an edit-distance lower bound.
- [ ] macOS repaints the whole window every frame (`native/swift/Sources/Deview/Runtime.swift:139-140`). Redraw only when the frame, bounds or a picture stamp change, and cache scaled pictures.
- [x] Windows `Thread.Sleep(16)` sleeps about 30 ms at the default timer resolution, and a hidden process wakes about 34 times a second forever (`src/DiffEngineViewer.Windows/FormsViewerWindow.cs:65`). Use `MsgWaitForMultipleObjectsEx`, with a longer timeout while hidden.
- [ ] Windows image panes rescale from full resolution and redraw the checkerboard on every paint (11 to 40 ms per image), and decode on the UI thread (`ViewerCanvas.cs:385-420`, `ImageCache.cs:56-71`). Cache the composited scaled bitmap per path, stamp and size.
- [x] All three heads lay out each row's full text though only about 35 cells fit (`ViewerCanvas.cs:503-508`, `src/DiffEngineViewer/Native/ScreenPayload.cs:164-177`); a 1 MB minified line costs about 0.8 s per paint on Windows. Truncate to the visible columns before drawing or marshalling.
- [x] raylib busy-waits the last 5% of every frame: `set(SUPPORT_PARTIALBUSY_WAIT_LOOP OFF CACHE BOOL "" FORCE)` in `native/CMakeLists.txt`.
  - That line alone changed nothing: `build-native` after #884 produced byte-identical binaries. The misparse behind "The Linux viewer never showed a frame" had `SUPPORT_BUSY_WAIT_LOOP` on too, which takes precedence and would have spun through the whole of every frame's wait once frame control ran it. Both are off now.
- [x] `InlineStaging.Clear` walks the `obj` tree and re-reads and parses every staged `.inlinepatch` on each verification (`src/DiffEngine/Inline/InlineStaging.cs:94-192`). Cache per directory keyed on `LastWriteTimeUtc`.
- [x] Tray: `SafeMove`'s 8 × 400 ms retry runs on the UI thread even for failures that cannot clear, such as a read-only target or a missing directory (`src/DiffEngineTray/Tracker.cs:535-585`, `FileEx.cs:73-90`). Retry only sharing violations.
- [x] Tray: the 2 s scan re-reads every equal-size, different pair from scratch (`Tracker.cs:81-97`, `FileComparer.cs:18-57`). Cache length and write time with the last result.
- [x] `PiperClient` has no unowned-port memory for 3492, and `TrayAvailable` is cached at type init, so after the tray exits every send pays a refused connect (`src/DiffEngine/Tray/PiperClient.cs:138-153`, `PendingFiles.cs:47-49`).


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
