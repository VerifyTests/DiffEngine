# Review todo

Findings from a review of `main` at 991bc480 (2026-10-03). The list from the review at 4244ebe6 is closed, so this one weights what has landed since: documents and maps, the text diff, zoom and pan, the remembered window and views, and pictures decoded off the UI thread. The five bugs that review found in the viewer's model are fixed and gone from this list.

Most of what follows came from six reviews run alongside it, one per area: the library, the inline patcher, the tray, and the Windows, Linux and macOS heads. The word after each title says how far the item was taken.

- **reproduced**: run and seen by me. Either the API involved, or the repo's own sources compiled into a scratch console project outside the repo. No test in the repo yet.
- **measured**: timed by me in that same project. Release, net10.0, this machine.
- **read**: confirmed by me reading the code path, not run.
- **reported**: found by one of the six reviews and not rerun by me. What follows the word is the reviewer's own evidence: *ran* is a probe of theirs outside the repo, *measured* a timing of theirs, *read* a trace through the code, and *plausible* a reading that rests on something they could not run, with what would settle it. For each of these I checked that the code it quotes is in the tree as quoted, and nothing more.
- Nothing under a Linux or macOS heading has been run by anyone: the C++ and Swift cannot be built from Windows.


## Bugs

### Library

- [ ] **A viewer that cannot start is reported as launched, so an inline snapshot is neither queued nor staged** (read; both triggers reported: ran)
  - `src/DiffEngine/Viewer/ViewerLaunchGate.cs:119-135` and `:172-188`: `WaitForBind` returns nothing, so a launch that never bound the port is still `Launched`, which `DiffRunner_Inline.cs:104-109` turns into `InlineResult.Queued`. Verify then stages nothing.
  - A resolved copy older than the library. The search order is the global tool, the tray's copy, then the bundled one, and a 20.3.1 copy exits 2 on `--payload` ("Unknown argument"), which first shipped in 20.5.0. With no tray running, every failing inline snapshot starts a process that exits at once, waits out the five second `BindWait` holding the gate, and reports `Queued`, until `MaxInstance` is used up. The temp `.inlinepatch` is never deleted.
  - No .NET 10 Desktop runtime: the apphost exits 131, or shows a dialog and never exits, with the same result.
  - `ViewerLaunchGateTests.AViewerThatNeverAnswersDoesNotHoldTheGateForever` pins `Launched` for a viewer that never binds, on the reasoning that the work went over on the command line. Neither case here reaches code that could stage.
  - The NuGet fallback picks the most recently written version folder, not the highest (`FallbackViewerDirectories.cs:94-103`, `WildcardFileFinder.cs:75-77`), so it can also choose a copy from before 20.5.
  - Fix: have `ViewerLauncher` return the `Process`, and stop `WaitForBind` as soon as it has exited without the port being owned, reporting `Failed` so the caller stages. Do not resolve a copy older than the launch contract, or try the bundled copy first for launches the library makes itself.

- [ ] **`DiffRunner.LaunchProcess` hands the test host's handles to tools declared `UseShellExecute: false`** (read; the effect reported: ran, with a stand-in child)
  - `src/DiffEngine/DiffRunner.cs:371-380` starts the tool with `UseShellExecute = tool.UseShellExecute` and nothing redirected. `MsWordDiff`, `MsExcelDiff` and `Cursor` are declared `false`, so they inherit the host's stdout pipe, which is what `ViewerLauncher` was rewritten to avoid.
  - A host that starts an 8 second child this way and exits has its redirected stdout reach EOF after 8.3 s, against 0.1 s with ShellExecute. `diffword` waits until Word is closed, so `dotnet test` with a failing `.docx` would not return until then. On macOS and Linux every long lived tool is in the same position, ShellExecute or not.
  - What would settle it end to end: `dotnet test` with a failing docx while Word stays open.
  - Fix: on Windows start these through ShellExecute with `WindowStyle = Hidden`. On Unix reuse `ViewerLauncher.StartInfo`'s redirect and close for GUI tools, not terminal ones such as nvim.

- [ ] **A launched viewer pins the test host's working directory** (reported: ran)
  - `src/DiffEngine/Viewer/ViewerLauncher.cs:151-169` sets no `WorkingDirectory`, so the viewer inherits the host's, commonly the test project's `bin/<configuration>/<framework>`. That directory cannot be deleted while the viewer lives, and a viewer can sit hidden behind the tray for the session: `git clean -xdf`, or removing a worktree, fails with no visible culprit. `DiffRunner.cs:373-380` has the same shape.
  - Fix: set `WorkingDirectory` to the viewer's own folder or temp. The paths it is handed are absolute.

### Inline snapshots

- [ ] **F#: accepting into a call that does not start its line writes source the compiler rejects** (reproduced)
  - `src/DiffEngine/Inline/InlinePatcher.cs:498-507` (`TryAppend`), `:1382-1392` (`RenderArgument`), `:1617-1638` (`IndentForSpan`): both indents are measured from the line's leading whitespace, and F#'s offside rule needs the continuation right of the column the expression starts at.
  - Append onto `do! Verifier.Verify("findPerson", person).ToTask()`, which is the shape of Verify's own Expecto sample, writes `.Snapshot("c").ToTask()` on the next line at the column of `Verifier`: `error FS0010: Unexpected symbol '.' in expression`. Set to a value of two lines after `let! _ =` fails the same way, at `.ToTask()`.
  - By the reviewer's runs under `dotnet fsi`: after `do!`, Append fails and Set compiles. After `let! x =`, `let x =` and a one line `let f () =`, both fail. A call that starts its line, and one after `return!`, compile. `FsCompilerRoundTripTests` only uses calls that start their line.
  - Fix: for F#, when the call is not the first token on its line, measure from the column the call expression starts at. Add the `do!`, `let!`, `let` and one line shapes to `FsCompilerRoundTripTests`.

- [ ] **C#: Append lands after `ConfigureAwait`, `ToTask` or `GetAwaiter`, and `CanAnchor` says the site can host it** (reproduced)
  - `InlinePatcher.cs:478` and `:689-740` (`WalkChain`): `SourceLanguage.ChainTerminator` is null for C#, and only F# has `"ToTask"` (`FsLanguage.cs:31`), so the end of the chain is always where it is inserted.
  - `await Verify(value).ConfigureAwait(false);` becomes `await Verify(value).ConfigureAwait(false)` then `.Snapshot("new");`, which is CS1061: a `ConfiguredTaskAwaitable` has no `Snapshot`. `anchorOnly` returns Applied before the chain is walked (`:473-476`), so Verify's `InlineAnchor.CanHost` declares the verification inline.
  - Fix: treat `ToTask`, `ConfigureAwait` and `GetAwaiter` as terminators in both languages, and insert in front of the first.

- [ ] **Queue identity is the line alone: a partial accept then a re-run duplicates entries, and a settle can take another test's** (reported: read)
  - `InlineQueue.cs:44-59` (`Enqueue`), `:241-248` (`Settle`), and `InlineStaging.cs:125-127` for staged trios.
  - Entries for A, B and C at lines 10, 20 and 30 of one file. Accepting A adds five lines, and the re-run sends B and C at 25 and 35. No key matches, so the queue holds B|20, C|30, B|25 and C|35. If B's content changed, accept-all applies the stale B|20 and then refuses the fresh one.
  - After the same accept, a passing call that moved from line 15 to line 20 settles `file|20`, which is B's stale key. A direct hit is not checked against the member or the value.
  - Fix: on a direct hit where both members are known and differ, treat it as a miss unless `IsSettledBy(value)` holds. In `Enqueue`, when the key misses, fold into the single entry with the same file, member and anchor. Longer term, have `InlineApplier` report the line delta and rebase the hints of what is left for that file.

- [ ] **Clearing staged trios is never scoped to a framework in practice** (reported: plausible)
  - `InlineStaging.cs:142-151`, `InlinePatchFile.cs:9-18` and `:31`, `RuntimeMoniker.cs:5`. Verify calls `InlineStaging.Clear` with no origin, and cannot supply one because `RuntimeMoniker` is internal. It also stages through `InlinePatchFile.Write` with `patch.Framework` null, which any origin clears.
  - A multi-targeted project, no viewer, and a snapshot that differs per framework: net8 fails and stages a trio, net9 passes and its settle deletes it.
  - What would settle it: a two framework run with `DiffEngine_InlineViewer=false`.
  - Fix: stamp `RuntimeMoniker.Current` in `InlinePatchFile.Write` when the patch has no framework, and expose a clear that uses the current moniker.

- [ ] **Append takes the first entry point in the member, even one that already has a Snapshot** (reproduced)
  - `InlinePatcher.cs:456`, `:864-884` (`TryFindCall`), `:487-496`.
  - `await Verify(a).Snapshot("A");` then `await Verify(b);` in one member, with a hint that an accept higher in the file made stale. The walk restarts at the member and yields `Verify(a)` first, and the answer is NotFound, "The call near line 12 already has a Snapshot call. Re-run the test.", with `Verify(b)` plainly there. A single accept drops the entry.
  - Fix: in `TryAppend`, take the first entry point with no chained Snapshot, and give today's answer only when every candidate has one.

- [ ] **Remove on `settings.Snapshot(...)` leaves `settings;`** (reproduced)
  - `InlinePatcher.cs:574-614`: the only shape check is `source[start - 1] != '.'`, and the splice removes `.Snapshot("old")` and keeps the receiver. `settings.Snapshot("old");` becomes `settings;`, which is CS0201. `VerifySettings.Snapshot` is public API, and Verify sends a Remove for it under `NotInline()`.
  - Fix: when the receiver starts the statement and the call is followed by `;`, remove the statement's line. Otherwise report NotFound.

### Tray

- [ ] **"Accept all" deletes the verified file a move in the same sweep just wrote** (read; reported: ran)
  - `src/DiffEngineTray/Tracker.cs:873-889`: `AcceptAll` accepts every move and then every delete, and `AddMove` (`:165-227`) drops no tracked delete for its target. The wire sweep at `:1004-1040` is the same.
  - A delete for `Foo.verified.png` is tracked and not accepted. A later run fails on that target, and a move onto it is tracked beside it. Accept all moves the received file into place and then deletes it: both files gone, with no warning.
  - The stale delete is still there with any library before 20.4.0, which has no `SettleDelete`, when a viewer owns the queue, and when the settle was skipped by the ten minute unowned-port memory.
  - Fix: in `AddMove`, drop a tracked delete whose file is the move's target. As a second guard, have both delete sweeps skip a file that a tracked or just accepted move targets.

- [ ] **Accept-all lists the deletes after the snapshot sweep, so one that arrived mid-batch is carried out without its patch** (reported: read)
  - `Tracker.cs:411-436`, `:447-465`, `:880-889`, `:1022-1037` and `OwnedInlineHost.cs:427-441`, `:598-613`: the snapshot keys are taken when the batch starts, and the deletes are read when their turn comes.
  - A "snapshot moving inline" pair lands while a batch is applying. Its patch is not in the batch, but its delete is in the list taken afterwards, so the verified file goes while the patch is only pending.
  - Fix: take the delete keys when the batch begins and carry out only those, as the viewer's own batch does (`src/DiffEngineViewer/ViewerSession.cs:994`).

- [ ] **An owner that cannot be asked reads as "nothing pending", so the tray's deletes go ahead** (reported: ran)
  - `Tracker.cs:447-457`: `if (inline.List().Count == 0) { return false; }`, and `RemoteInlineHost.List` is `TryList(out var pending) ? pending : []` (`RemoteInlineHost.cs:51-72`).
  - A viewer owns the queue and holds a patch, the tray holds the paired delete, and the viewer does not answer `List` within 500 ms: cold starting, or wedged. Accept all deletes the verified file with the patch never tried.
  - Fix: let the host tell "port not held" from "held but no answer", and treat the second as refused: deletes held, user told.

- [ ] **The tray's UI thread is MTA, so Debug view's Copy always throws** (reported: ran)
  - `src/DiffEngineTray/Program.cs:5` is `static async Task Main()` with no `[STAThread]`. `Clipboard.SetText` (`DebugForm.cs:92-109`) throws `ThreadStateException`, which `catch (ExternalException)` does not catch, so it reaches WinForms' Continue or Quit dialog and nothing is copied.
  - Fix: `[STAThread] static void Main()` that calls `Inner().GetAwaiter().GetResult()`, or `content.SelectAll(); content.Copy();`.

- [ ] **A tray that owns the queue does not stage it at logoff or shutdown** (reported: plausible)
  - `OwnedInlineHost.cs:746-782`, `Program.cs:129-139`: `Persist()` only runs when `Inner` unwinds after `Application.Run()` returns. Nothing in the tray handles the session ending, which the viewer does. For a tray started at login, that is how its life usually ends.
  - What would settle it: log off with one pending inline snapshot, then look for the staged `.inlinepatch`.
  - Fix: a hidden `NativeWindow`, or `SystemEvents.SessionEnding`, that calls `Persist()` on `WM_ENDSESSION`.

- [ ] **A `Move` or `Diff` over 3493 for a pair already tracked replaces its tool with the tray's own choice for the extension** (reported: ran)
  - `Tracker.cs:198-226`, `:234-246`: the update always rebuilds with the incoming `exe`, and null falls to `DiffTools.TryFindByExtension`. "Open diff tool" on a viewer pair starts `DiffEngineViewer --diff`, which cannot bind and forwards `Diff` to the tray.
  - Before: `DiffEngineViewer.exe CanKill=False IsViewer=True IsOpen=True`. After: `BCompare.exe CanKill=True IsViewer=False IsOpen=False`. The "Accept open" hot key then skips a pair that is on screen in the viewer.
  - Fix: when the incoming `exe` is null, keep the tracked `Exe`, `Arguments`, `CanKill`, `KillLockingProcess` and `IsViewer`, and refresh only the target.

### Viewer, Windows head

- [ ] **A decode dropped because its picture left the screen is never retried: a spinner for good** (read; reported: ran)
  - `src/DiffEngineViewer.Windows/ImageCache.cs:240-257` (`Loaded`): a decode that lands for a path no longer wanted is disposed, and the method returns before `pending.Remove(path)`. `Get` (`:194-198`) then reads the entry in `pending` as already on its way and starts nothing, and `Loading` stays true.
  - Step past an image entry or a document page before its decode lands, then come back: Tab twice, hold Tab, `]` `]`. Both panes show a spinner until the file's stamp changes, repainted 25 times a second. A decode takes 23 ms for a 1920x1080 png and 89 ms for a 2550x3300 page against a 16 ms frame, so holding Tab through a queue of pictures sticks nearly every one.
  - `ADecodeForAPictureNoLongerOnScreenIsDropped` pins the drop and never comes back to the path.
  - Fix: in `Loaded`, remove the pending entry once the stamp matches and before the `wanted` test, or have `Keep` remove pending entries for paths it no longer wants.

- [ ] **The first window is centred for its unscaled size and then scaled in place** (reported: ran)
  - `ViewerForm.cs:140-141`, `:234-241`, `:374-392`: WinForms centres before `OnHandleCreated` assigns `ClientSize = InitialClientSize(...)`. At 125% on a 3440x1380 working area the margins are left 1161, right 886, top 316, bottom 142. On 1920x1080 at 150% the same arithmetic puts the footer under the taskbar, and `Placement` then remembers those bounds for every later run.
  - Fix: `CenterToScreen()` straight after the `ClientSize` assignment in the `!sized` branch.

- [ ] **`Raise()` un-maximises a window that was minimised from maximised, and that is then remembered** (reported: ran)
  - `ViewerForm.cs:482-492`: `if (WindowState == Minimized) { WindowState = Normal; }`. Maximise, minimise, a snapshot arrives: the window comes back at its normal size, and the next hide or close saves it that way.
  - Fix: `WindowState = maximized ? FormWindowState.Maximized : FormWindowState.Normal;`

- [ ] **One picture path on both sides, panes a pixel apart: the composite is rebuilt forever** (reported: ran)
  - `ImageCache.cs:297-338`, `:340-365` keep one composite per path. Identical documents share their page pngs, and with an odd panes width the two sides ask for sizes a pixel apart (`ViewerCanvas.cs:517-518`, `:581-583`): 14,609 composites in 2 s with a trivial builder, one side always the stretched copy.
  - Fix: give both panes' picture space the same width, or keep the composite by size.

### Viewer, Linux head

- [ ] **Caps Lock turns `a` (accept) into accept-all** (read)
  - `native/src/deview.cpp:950-974`: letters are decided by the character typed, `'a'` accept and `'A'` accept-all. Caps Lock produces `'A'` without Shift, and `ViewerSession.BeginAcceptAll` starts with no confirmation. The other heads decide by the Shift modifier. With Caps Lock on, d, v, q, n, p, m, r and j also do nothing.
  - Fix: lower-case the character before the switch, and choose accept-all from `IsKeyDown(KEY_LEFT_SHIFT) || IsKeyDown(KEY_RIGHT_SHIFT)`.

- [ ] **The footer has no overflow handling** (reported: read, by arithmetic against the Linux baselines)
  - `deview.cpp:1987-2027`: every button is `SameLine()`d, and the status is drawn after the last when nothing is left. A paged document in the queue needs 1199 px of buttons in a 1084 px content width: "Zoom in" starts at x=1128, off the window, and the status at 1213. In file mode the status gets 54 px. The status line is where page numbers, draw failures, the selection, accept-all progress and accept failures are said.
  - Fix: give the status its own line, and wrap buttons that would pass the content edge. The title row (`:1617-1624`) has the same shape.

- [ ] **No fallback font: every character JetBrains Mono lacks draws as the replacement glyph** (reported: read)
  - `deview.cpp:2128-2138` is the only `AddFont` call. CJK, Hangul, Arabic, Hebrew, Thai and emoji in a snapshot cannot be reviewed on Linux. Windows and macOS fall back to system fonts.
  - Fix: merge system fonts with `MergeMode`, found by fontconfig or well known Noto and DejaVu paths, and clip each segment that is not simple to its own cells.

- [ ] **Text cannot be selected in the right pane while the left pane's visible rows are all filler** (reported: read)
  - `deview.cpp:1244-1260` (line 1253): the start test rejects on `leftHit.textLeft < 0.0f` before looking at which pane was pressed, and `textLeft` is only set by a row that is not filler. That is every pending delete, whose left side is empty, and any scroll position inside a long removed block.
  - Fix: test `leftHit.textLeft` only when the press is in the left pane, or derive it from the gutter width.

### Viewer, macOS head

- [ ] **Keys and clicks share one slot per kind, so a slow frame drops or reorders them** (reported: read)
  - `native/swift/Sources/Deview/ViewerView.swift:130,143,283,296,361`, `MainMenu.swift:15,26,37`, `Runtime.swift:350-355`: `pump()` dispatches every queued event before returning, and each handler overwrites one field. With the loop busy, Tab then `a` accepts the entry the reader meant to skip, and `d` then a click on another row discards the clicked row, because `Apply` runs the click before the key. The Windows head fixed this with a queue (`ViewerForm.cs:95-116`).
  - Fix: queue discrete events in `Runtime` and hand over one per `deview_poll_input`. No ABI change.

- [ ] **The footer has no overflow handling: the status is drawn over the buttons, and a document's buttons fall off the window** (reported: read, by arithmetic against the OSX baselines)
  - `Renderer.swift:346-371`. At the default 1100 pt an image pair in queue mode has buttons ending at x=1015 and "images differ" starting at 977. A paged document's eleven buttons need 1405 pt in queue mode, so "Zoom out" and "Zoom in" cannot be clicked.
  - Fix: clip the status to the space right of the last button, and give the buttons a second row or less padding when they overflow.

- [ ] **AppKit's nested tracking loops starve the managed loop** (reported: read; what it looks like needs a Mac)
  - `Runtime.swift:269-270`, `:279-298`, `:350-355`: the scroller knob is tracked inside `NSApp.sendEvent`, so `deview_present` does not return and no frame is presented. Dragging the knob moves it without scrolling the panes until release, and a live resize draws the old slice into the new size. `ILoopHooks` says the native heads have no modal loops.
  - Fix: track the knob without the modal loop, or add a frame callback to the ABI that Swift calls from `scrolled` and during a live resize.

- [ ] **macOS draws JetBrains Mono's code ligatures** (reported: read, from a committed baseline)
  - `Renderer.swift:217-233`, `:919-929`: Core Text applies `calt` by default. In `PixelTests.Images.OSX.verified.png` the title's `<>` is one glyph. `!=`, `<=`, `=>`, `->` and `==` in snapshot text are drawn as ligatures, in a tool whose job is to show which characters a snapshot holds.
  - Fix: create the `CTFont` with `calt` and `liga` off through `kCTFontFeatureSettingsAttribute`, then re-approve the OSX baselines.


## Performance

### Viewer model

- [ ] **The screen is rebuilt every frame, including frames where the state is the same object** (measured)
  - `src/DiffEngineViewer/ViewerProgram.cs:464-465` (and `ModalFrame`, `:380`): `ScreenBuilder.Build(host.State)` runs sixty times a second, ten when hidden. `SessionState` is immutable and only replaced by `SessionHost.Mutate`, so an unchanged reference is an unchanged screen.
  - `QueueProjection.Rows` builds a label, a group key and a tooltip for every entry in the queue before `Visible` slices out the few on screen (`src/DiffEngineViewer/QueueProjection.cs:102-199`, `:237`). Per call: 100 entries 0.1 ms and 123 KB, 500 entries 0.57 ms and 618 KB, 2,000 entries 0.72 ms and 2.3 MB. At sixty frames that is 7, 36 and 136 MB of garbage a second from a window nobody is touching.
  - `SelectionText.Summary` walks every selected row every frame (`src/DiffEngineViewer/SelectionText.cs:146`), flattening each twice. Ctrl+A on a tab indented file: 20,000 lines 3.1 ms and 5.6 MB a frame (330 MB a second), 100,000 lines 15.8 ms and 28 MB a frame, which is the whole frame budget for as long as the selection stands. Nothing selected: 0.001 ms.
  - Downstream of it, the WinForms head compares the new screen with the last field by field (`src/DiffEngineViewer.Windows/ViewerForm.cs:789`) and the native heads re-encode it (`src/DiffEngineViewer/Native/NativeViewerWindow.cs:101`).
  - Fix: keep the last state and its screen in the loop and rebuild only when the reference changed. `ViewerForm.Same` and `ScreenPayload.Build` can then return early on the same screen reference. That removes all of the above without touching what any of it computes.

- [ ] **The text diff is quadratic when the two sides share little, and the viewer runs it before it answers** (measured)
  - `src/DiffEngine/TextDiff/MyersDiff.cs:159`: the search runs to `maxD` with no bound, and `LineDiff.Build` (`src/DiffEngine/TextDiff/LineDiff.cs:68`) hands it every line. Nothing in common: 10,000 lines a side 226 ms, 20,000 866 ms, 40,000 3.5 s. One percent changed, 400,000 lines: 175 ms.
  - "Nothing in common" is an ordinary snapshot change: a serializer setting that re-indents every line. A 40,000 line re-indented JSON takes `TrackedEntry.ForMove` 3.5 s, and 80,000 lines 14 s.
  - `MessageHandler.TrackMove` builds the entry, diff included, before the `Diff` or `Move` is answered (`src/DiffEngineViewer/Ipc/MessageHandler.cs:54`). The synchronous client gives up at 3 s (`src/DiffEngine/Protocol/ViewerClient.cs:64`), so `PendingFiles.AddDiff` (`src/DiffEngine/Tray/PendingFiles.cs:134-144`) falls to the launch gate, finds the port owned, sends again, times out again and returns `NoDiffToolFound`, while the viewer diffs the pair twice on two pool threads and then replaces the first entry with the second. The async path has 30 s.
  - Fix, in order of value:
    - Before Myers, drop the lines that occur on one side only and mark them changed. `LineInterner` already says which: a received id of `expectedLines.Length` or more never occurs in expected, and one pass over the received ids marks the expected ones that do occur. The longest common subsequence is unchanged, so the result is still minimal. Both cases above become linear.
    - A cost cap for what is left. The same lines in another order (40,000 lines: 6.9 s) have nothing unique to drop. Past the cap, report the remaining block as removed then added.
    - Answer the pair before diffing it: queue the entry with its sides unread and fill it in on `TrackedWatch`'s thread, the way a document arrives `Reading`.

- [ ] **"Accept all in" a group still applies the whole group in one transition on the render thread** (read)
  - `src/DiffEngineViewer/ViewerProgram.cs:700-711`: only `AcceptAll` is handed to `AcceptAllRunner`. `AcceptGroup` goes to `ViewerSession.Apply` inside `host.Mutate`, where `AcceptGroup` (`src/DiffEngineViewer/ViewerSession.cs:789`) runs `InlineApplier` for every member and `SweepTracked` (`:1277`) moves or deletes every file, each move retrying for up to a second when the target is held (`src/DiffEngineViewer/ViewerActions.cs:71-87`).
  - In a queue with one solution, that header's "Accept all in" is the whole queue: the freeze `AcceptBatch` was written to remove, with the lock held so every arriving `Inline`, `Diff` and listing waits behind it.
  - Fix: let `BeginAcceptAll` take the keys to sweep, and send a group through the same runner.

- [ ] **The right side of a document waits for every page of the left** (measured)
  - `src/DiffEngineViewer/Documents/DocumentWatch.cs:235-243`: `Draw` renders the first side without pages to completion and returns. The right side starts on the next pass.
  - 100 A4 pages take 4.3 s here and the first lands after 87 ms. So the left page is on screen at once, and the right pane spins for 4.3 s, and which pages differ is unknown until both are done.
  - Fix: draw both sides at once on two tasks. Two PDFs then take turns at PDFium's lock a page at a time, and two Office files use two cores. The timeout, its `progressed` clock and `generation` become per job.

- [ ] **Every pending file is statted five times a second, and the owning watch never slows down** (measured)
  - `src/DiffEngineViewer/TrackedWatch.cs:27-55` and `src/DiffEngineViewer/Ipc/OwnerLink.cs:344-432`. One pass over 1,000 pending moves is 2,000 stats and takes 25 ms here, so 125 ms of every second. The class doc's "a queue is small enough that the difference is not measurable" holds to about a hundred.
  - `OwnerLink` drops to one pass a second when the window is hidden and `DocumentWatch` stops. `TrackedWatch` has no `Hidden`, and an owning viewer hidden behind a tray keeps its 200 ms for days.
  - Fix: give `TrackedWatch` the hidden interval. If large queues matter, stat the entry on screen every pass and the rest in turn.

### Library

- [ ] **The refused connect is still paid once per test process, and half a second per gated call while nothing can be launched** (reported: measured)
  - `src/DiffEngine/Protocol/ViewerClient.cs:211-228`, `:257-281`, `:455-469` and `ViewerLaunchGate.cs:111`, `:164`, `:199-226`. A refused loopback connect is 2,029 ms on Windows. `IPGlobalProperties.GetActiveTcpListeners()` is 0.2 to 0.4 ms and reports a loopback listener correctly.
  - The unowned memory is per process and starts empty, so with no tray or viewer the first telling send of every test process blocks its thread for 2 s: 2 s on a one test inner loop that otherwise takes one, per framework, and again every ten minutes. `IsOwned` always connects and waits `ShortTimeout`, so a gated call with nothing owning the queue and nothing launchable costs 0.5 s under the gate, as does each `WaitForBind` poll.
  - Fix: ask the listener table before connecting, as `PiperClient.PortIsHeld` does for 3492, falling back to the connect when the table cannot be read.

### Inline snapshots

- [ ] **Every accept re-reads, re-lexes and rewrites the whole file** (reported: measured, in a simulation of the applier's IO and allocations)
  - `InlineApplier.cs:139-231`, `:327-375`, `SourceScan.cs:20`, `InlinePatcher.cs:127-130,137`. 500 applies to one 10,000 line, 633 KB file took 3.8 s with 499 gen2 collections, about 4 MB on the large object heap each. It bites on accept-all over a large test file, and once per new call site per run through `CanAnchor`.
  - Fix: a batch apply per file that reads once, applies in memory and writes once, still reporting each outcome. Short of that, pool the scan arrays.

- [ ] **`InlineStaging.Clear` walks the whole `obj` tree for every passing inline verification** (reported: measured)
  - `InlineStaging.cs:104-113`, `:288-360`. Per Clear, warm: 2.8 ms for Verify.Tests' obj (151 directories), 0.95 ms for DiffEngine.Tests', so one to three seconds per thousand passing inline verifications.
  - Fix: cache the directory list per `obj`, invalidated in process from `InlinePatchFile.Write` and `Persist` and with a short life for other processes. The comment at `:506-516` says why "nothing staged" is not cached, and a fix has to respect it.

### Viewer, Windows head

- [ ] **Rows are clipped to the pane's width in pixels, counted as characters** (reported: measured)
  - `src/DiffEngineViewer.Windows/ViewerCanvas.cs:917-933`: `RowText.Clip(..., bounds.Right - left)` keeps as many characters as the pane has pixels, and GDI+ lays out every one. 72 rows of 400 character lines cost 11.0 ms a paint against 2.2 ms clipped to the 54 that show, and at 1600 px a pane 48.2 ms against 8.7 ms. Every wheel notch and every frame of a selection drag is one such paint, so files with long lines scroll at 20 frames a second or worse.
  - Segmenting runs over the whole line before anything is clipped: a 1 MB line holding one non-ASCII character costs 52 ms a row a paint.
  - Fix: clip to `ceil((bounds.Right - left) / Advance) + 1` characters, and cut the flattened text at `CellGrid.Index(text, visibleCells)` before calling `Segments`.

- [ ] **An enlarged picture below its own size is rescaled from full resolution on every paint** (reported: measured)
  - `ViewerCanvas.cs:614-656`: a 4000x3000 pair costs 44.5 ms a paint at 150% and 30.5 ms at 200%, on every frame of a pan and every wheel notch over a document's text. `HighQualityBilinear` prefilters, so it costs by source pixels.
  - Fix: when the placement is narrower than the picture, compose and cache it as the fitted one is. Cheaper: plain `Bilinear` when the reduction is under two times.

### Viewer, Linux head

- [ ] **The checkerboard behind a picture is tessellated again as thousands of quads every frame** (reported: read; the cost is an estimate)
  - `deview.cpp:1334-1355`, drawn through `RenderTriangles` (`:817-851`): one dark quad per 128 square pixels, about 4,400 for two panes at the default window and about 53,000 maximised at 4K, sixty times a second, for opaque pictures too.
  - Fix: one quad with a small two tone texture set to repeat, skipped when the decoded image has no alpha.

- [ ] **Everything is rebuilt, marshalled and redrawn at 60 Hz when nothing changed** (reported: read; the managed part measured)
  - `deview.cpp:2114` (`SetTargetFPS(60)`), `:2146-2191`, with `ViewerProgram.cs:464-470` and `ScreenPayload.cs:171-237`. Under llvmpipe or a remote session each swap is a full window software raster.
  - `ScreenPayload.AddSegments` recounts the bytes before each segment with `Encoding.UTF8.GetByteCount(text.AsSpan(0, segment.Start))`, which is quadratic in a row's segments: rows of CJK cost 6.5 ms a frame at 4K, 2 ms of it that scan. This one is shared with the macOS head.
  - Fix: skip `ScreenBuilder.Build` and `payload.Build` when the state is the one last presented, which the first item under Viewer model does. In `deview_present`, skip the frame when its bytes match the last, no input arrived and nothing is pending. Carry a running byte offset in `AddSegments`.

### Viewer, macOS head

- [ ] **A spinner tick runs the whole draw again, and text outside Latin costs one CTLine per character** (reported: read; the cost is an estimate)
  - `Runtime.swift:227-237`, `ViewerView.swift:54-65`, `Renderer.swift:443-455`, `:914-931`, `:952-963`: only the spinner's rectangle is repainted, but `renderer.draw` still builds about 150 attributed strings and lines roughly twenty times a second while a page is being drawn. `CellGrid.Simple` leaves out box drawing, arrows, typographic punctuation and CJK, so each such character is its own segment and its own line.
  - Fix: skip work outside the context's clip, cache the lines of single clusters, and widen `Simple` to ranges the embedded font draws one cell wide.

- [ ] **An enlarged picture below its own size is resampled at `.high` on every pan frame** (reported: plausible)
  - `Renderer.swift:598-605`: `context.draw(picture, in: all)` with no cached copy. A 2880 wide screenshot at 150% is resampled for both panes on each frame of a drag.
  - What would settle it: Instruments on a Mac.
  - Fix: below its own size, build a scaled copy on the work queue as the fitted one is, or draw at `.low` while dragging.


## Smaller

### Viewer model

- [ ] A pair sent again unchanged is still read and diffed before it is found to be unchanged: `MessageHandler.TrackMove` builds the whole entry, and `ViewerSession.EnqueueTracked` compares after. The reader is no longer moved, but a large pair pays the diff on every run. Fix: read the two sides, compare them with the queued entry's, and build an entry only when they differ. (read)
- [ ] `QueueProjection.Order` runs twice per transition: `Project` orders (`ViewerSession.cs:1530`) and `Rebuild` (`:1490`) and `Sync` (`:279`) order its result again. (read)
- [ ] Opening a context menu builds each side's whole text to ask whether it is empty: `SelectionText.All(entry, side).Length > 0` in `src/DiffEngineViewer/MenuState.cs:98` and `:117`. Megabytes per right-click on a large file. (read)
- [ ] `FileSide.ReadBytes` copies every file twice, through a growing `MemoryStream` and then `ToArray` (`src/DiffEngineViewer/FileSide.cs:105`). The length is known. (read)

### Library

- [ ] Linux: an exported `COLUMNS` truncates every command line `ProcessCleanup` reads. `LinuxOsxProcess.cs:92` runs `ps -o pid,command -x`, and procps lets `COLUMNS` override the unlimited width it uses when stdout is not a terminal, so with `COLUMNS=80` a running tool is never detected or killed. Fix: add `-ww`, which procps and Apple's `ps` both accept. (reported: read, against the procps source)
- [ ] A `SendAsync` the caller cancelled is recorded as "port unowned": `ViewerClient.cs:372-384`, `:433-444`. `token.Register(() => Abort(client))` closes the client, the exception that follows is not an `OperationCanceledException`, and `Found(endpointPort, false)` silences settles and moves against a live owner for ten minutes. Not reachable from Verify today, which passes no token. Fix: `cancel.ThrowIfCancellationRequested()` on entry, and no `Found(false)` when the token is cancelled. (reported: plausible)
- [ ] With an owner present, every passing inline verification is a TCP connection that leaves a port in TIME_WAIT for two minutes (`DiffRunner_Inline.cs:136-145`, `ViewerClient.cs:276-292`): 0.284 ms each, but about 16,000 settles in two minutes across test processes exhaust the dynamic range, and the failed connect is then remembered as unowned. Fix, only if suites that size matter: list once and skip settles while the owner holds nothing for this framework, or keep one connection open. (reported: measured)
- [ ] `ViewerServer.Listen` (`ViewerServer.cs:89-97`) has `catch (SocketException) { continue; }` with no delay, so an accept failure that persisted would spin a core. Whether one can persist was not established. (reported: plausible)

### Inline snapshots

- [ ] A Remove applied twice can take a sibling's literal: `InlinePatcher.cs:652-656`. With `await Verify(a)` over `.Snapshot("dup");` and `await Verify(b).Snapshot("dup");` under it, the first apply pulls the next line up, and the second, from another framework or another case of an `IgnoreParameters` test, finds a Snapshot call on the hint line, so `RemovedAtHint` is false and `Verify(b)` loses its literal. Fix: do not pull the following text up, or have Verify skip the Remove when the source file is newer than the test assembly. (reported: read)
- [ ] The F# lexer disagrees with the compiler inside block comments: `FsLanguage.cs:183-229`. `(* returns "*)" when closed *)`, `(* see "(*" *)` and `(* the (*) operator *)` are each one comment to F#, and the scanner closes or nests on what is inside the string. The usual result is NotFound for calls below. Fix: inside a comment step over string literals and `(*)`, and step over a double backticked identifier whole. (reported: read; what F# does was run under `dotnet fsi`)
- [ ] An F# snapshot whose value is the empty string loses its anchor over the wire: `InlinePatchFile.Build` writes `OriginalValue == ""` as an empty field (`:53-55`), and `TryParse` reads an empty field back as null (`:148-152`). The viewer then heads the pane "expected (new snapshot)", and the patcher falls back to the hint alone. Fix: write a marker for "present and empty", or a separate line saying a value is present. (read)
- [ ] Keys are recomputed per comparison: `PendingInline.Key` lowercases the path and formats a string on every `FindIndex` step (`PendingInline.cs:70`, `InlineQueue.cs:48,244,453,565`, `InlineStaging.cs:366-367`). A few hundred milliseconds across a run with hundreds pending. Fix: compute the key once per entry. (reported: read)

### Tray

- [ ] `Process` objects are never disposed for moves that cannot be killed: `Tracker.cs:799-806`, `:945-950`, `ProcessEx.cs:19-32`. `DiffRunner` sends a process id for MDI tools too, `TryGet` forces a handle open, and `KillProcesses` returns at `if (!move.CanKill)` without disposing. One handle per tracked move until a gen2 finaliser. Fix: dispose, without killing, wherever a move finally leaves the dictionary. (reported: read)
- [ ] The scan can drop the wrong move, and one unexpected exception stalls it: `Tracker.cs:66-72`, `:97-114`, `AsyncTimer.cs:25-43`. `moves.TryRemove(tacked.Temp, out var removed)` removes by key, so a re-run that replaced the move between the scan's check and the removal loses its fresh entry and has its tool killed. Only `IOException` is caught around `FilesAreEqual`, and the handler shows a `MessageBox` on the timer thread. Fix: remove by key and value, and catch `UnauthorizedAccessException` beside it. (reported: plausible)
- [ ] "Discard (n)" waits up to 15 s on the UI thread when a viewer owns the queue: `Tracker.cs:836-851` calls `inline.DiscardAll(out var message)` inline, where `Discard` was moved to a worker for this reason. (reported: read)
- [ ] An exception thrown by a hot key action ends the tray: `HotKey/KeyRegister.cs:70-92`. One thrown from `IMessageFilter.PreFilterMessage` comes out of `Application.Run()` rather than reaching `Application.ThreadException`. Fix: try and catch around `action()`, and a catch in `LinkLauncher`. (reported: ran for the mechanism; no trigger found)
- [ ] The process id in a piper payload is trusted as the diff tool: `Tracker.cs:179-183`, `:205-211`. Libraries from before the `ProcessCleanup.StillRunning` fix can send a reused id, and the tray kills whatever holds it now on accept. Fix: compare the process image against the payload's `Exe` first. (reported: plausible)
- [ ] `ListingTag`'s `Fingerprint` (`OwnedInlineHost.cs:317-331`) rebuilds and hashes every tracked move per poll, about 0.5 MB of garbage five times a second while a viewer is attached. (reported: read)

### Viewer, Windows head

- [ ] Holding the scroll bar's arrow or its trough freezes the panes until release: `ViewerForm.cs:176-191` enters the modal frame only for `ThumbTrack`, and user32 tracks every part of the bar in the same loop. Fix: `EnterModal()` for any type other than `EndScroll` and `ThumbPosition`. (reported: ran)
- [ ] Alt chords fall through to the plain key commands: `ViewerForm.Map` (`:703-770`) gives Alt+A accept, Alt+D discard and Alt+Q quit. The same leak was closed for Control. Fix: return `CommandKind.None` when `Keys.Alt` is held, ahead of the Control branch. (reported: ran)
- [ ] Accept and Discard auto-repeat, and the repeats are queued: `ViewerForm.cs:703-721`, `:96-105`. Holding `a` past the repeat delay accepts entries the reader has not seen. Fix: drop repeats (bit 30 of `LParam`) for the commands `ViewerSession.ChangesQueue` names. (reported: plausible)
- [ ] The status label shows the middle of a status that does not fit: `ViewerForm.cs:21-32`, `:582-607`. A pdf in a queue leaves it 151 px, and a status of 344 px wraps to three lines in a 30 px label centred vertically. Fix: `AutoEllipsis = true`, with an alignment that keeps the start. (reported: measured)
- [ ] A minimised window still runs the loop at sixty frames a second: `FormsViewerWindow.cs:92` tests `form.Visible`, which stays true when minimised. Fix: `form.Visible && form.WindowState != FormWindowState.Minimized`. (reported: plausible)

### Viewer, Linux head

- [ ] Input is sampled as state once a frame, so a press and release that arrive together are never seen and several wheel events collapse into one: `deview.cpp:909-923`, `:2217-2220`. A touchpad two finger tap would never open a context menu. What would settle it: raylib 6.0's callbacks, and a tap on a real touchpad. Fix: chain GLFW's mouse button and scroll callbacks and feed ImGui from them. (reported: plausible)
- [ ] Ctrl+A and Ctrl+C are still by US key position, arrows and paging do not repeat when held, and no letter shortcut matches on a non-Latin layout: `deview.cpp:929-944`, `:976-985`. Fix: `IsKeyPressedRepeat` for navigation, and resolve the chords through `GetKeyName`. (reported: read)
- [ ] No display scale handling: no `FLAG_WINDOW_HIGHDPI` and no `GetWindowScaleDPI()`, so on a HiDPI X11 display everything is about half size (`deview.cpp:2075-2091`, `:2137`). What would settle it: a display with `Xft.dpi` 192. (reported: plausible)
- [ ] A queue row's context menu is not kept inside the window: the clamp at `deview.cpp:1929-1944` is inside `if (paneMenu)`, so the last row's menu is cut off at the default size. (reported: read)
- [ ] Hover never ends when the pointer leaves the window: `io.AddMousePosEvent(mouse.x, mouse.y)` is unconditional (`deview.cpp:915-916`), so a row stays highlighted and its tooltip appears with the pointer elsewhere. (reported: plausible)
- [ ] A picture larger than `GL_MAX_TEXTURE_SIZE` draws as a black box rather than as nothing (`deview.cpp:613-621`, `:706-712`), and pictures shrunk more than two times are sampled bilinear with no mipmaps (`:464-469`, `:1457-1464`), so thin lines and small text drop out. (reported: plausible, and read)
- [ ] Labels containing `##` are cut short, since ImGui hides everything from there on: pane headers, queue rows, menu items and buttons (`deview.cpp:1687-1688`, `:1716`, `:1738`, `:1961`, `:2003`). (reported: read)
- [ ] `GetWindowPosition()` every frame is a synchronous X round trip (`deview.cpp:299-320`), one network round trip a frame over forwarded X. (reported: plausible)

### Viewer, macOS head

- [ ] A picture landing during a repaint of the spinner alone is drawn clipped to the spinner's rectangle and never completed: `Renderer.swift:245`, `Runtime.swift:223-239`. A few in a thousand large images. Fix: have `draw` report that something landed, and turn that into a full redraw. (reported: read)
- [ ] `[` and `]` never match on layouts where they need Option, since `charactersIgnoringModifiers` yields the digit (`ViewerView.swift:412-433`): German, French, Nordic, Spanish and Italian layouts cannot turn pages by key. Fix: match symbols on `event.characters` first. (reported: read)
- [ ] A pan drag in a pane that cannot move on an axis resets that axis for the other pane: the report is clamped with the dragged pane's own extents (`Renderer.swift:164-174`, `ViewerView.swift:180-187`, and `deview.cpp:1587-1591` on Linux). Fix: report the frame's own centre unchanged on an axis the pane cannot move on. (reported: read)
- [ ] `picturesChanged` never settles when a picture has no room, so a window shorter than about 176 pt redraws at sixty frames a second (`Renderer.swift:485-489`, `:846-848`). Fix: a `contentMinSize`, or record the stamp when nothing is drawn. (reported: read)
- [ ] A notched mouse wheel may do nothing until ten slow clicks add up (`ViewerView.swift:310-322`), and control-click never opens a context menu (`:124-170`). What would settle them: a Mac with a wheel mouse. (reported: plausible)
- [ ] The pump waits out its full 16.7 ms after input, and nothing slows the loop when the window is occluded or miniaturised (`Runtime.swift:209-249`, `:257-276`). (reported: read)
- [ ] `Runtime.open` cannot fail (`Runtime.swift:57-91` always returns true), so with no console session, as over SSH, AppKit aborts the process after the port was bound and the patch is lost. What would settle it: a failing inline snapshot over SSH with nobody logged in. (reported: plausible)
