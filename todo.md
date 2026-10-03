# Review todo

Findings from a review of `main` at 991bc480 (2026-10-03). The list from the review at 4244ebe6 is closed, so this one weights what has landed since: documents and maps, the text diff, zoom and pan, the remembered window and views, and pictures decoded off the UI thread.

Most of it came from six reviews run alongside, one per area: the library, the inline patcher, the tray, and the Windows, Linux and macOS heads. Every bug found is fixed and gone from this list: the five in the viewer's model, and the twenty seven the six reviews found. What is left is what those fixes did not reach, the performance items, and the smaller ones.

File and line references are as of a01dfc1d, before the second round of fixes. The code they point at is unchanged, but lines below an edit have moved, so go by the names.

- **reproduced**: run and seen by me. Either the API involved, or the repo's own sources compiled into a scratch console project outside the repo. No test in the repo yet.
- **measured**: timed by me in that same project. Release, net10.0, this machine.
- **read**: confirmed by me reading the code path, not run.
- **reported**: found by one of the six reviews and not rerun by me. What follows the word is the reviewer's own evidence: *ran* is a probe of theirs outside the repo, *measured* a timing of theirs, *read* a trace through the code, and *plausible* a reading that rests on something they could not run, with what would settle it. For each of these I checked that the code it quotes is in the tree as quoted, and nothing more.
- **left by the fix**: said by whoever fixed the bug it sits under, about the part the fix did not reach.
- Nothing under a macOS heading has been run by anyone: the Swift cannot be built from Windows, and its fixes are first compiled by CI. The Linux fixes were built and run in an `ubuntu:24.04` container.


## Bugs

Nothing that was found as a bug is open. What follows is what the fixes left.

### Library

- [ ] A viewer that is alive and never binds the port is still reported as launched after `BindWait`, and its payload file stays. That is the apphost's "install .NET" dialog, which does not exit. At the gate it cannot be told from a viewer that is only slow. (left by the fix)
- [ ] A viewer that exits 1 or 4 has staged the patch itself, and the caller, told the launch failed, now stages it too: two trios in different `VerifyInline` folders until a passing run clears both. (left by the fix)
- [ ] A failed launch still spends a `MaxInstance` slot, so after five in one process the cap answers instead. (left by the fix)
- [ ] On macOS and Linux a tool started without ShellExecute still inherits the test host's streams. Nothing in the definitions tells a terminal tool, which needs them, from a windowed one: Neovim is declared `UseShellExecute: true` like the rest. (left by the fix)
- [ ] `DiffRunner.LaunchProcess` still starts a third party tool in the test host's working directory, which then cannot be deleted while the tool is open. Left alone because a tool resolves relative arguments against it and `ProcessCleanup` matches on those same strings. (left by the fix)
- [ ] None of the four tools now started through `WindowsProcess.StartInheritingNothing` (Word and Excel comparers, Cursor, VS Code) was itself run. A console exe, a windowed exe and a `.cmd` stood in for them. (left by the fix)

### Inline snapshots

- [ ] Verify has to change for the staged trios of a multi-targeted project to be cleared per framework: in `InlineEngine.Settle()`, call `InlineStaging.Settle(MappedSourceFile, inline.Line, inline.MemberName, VerifierSettings.IntermediateDir, SnapshotInSource)` in place of `ClearStaged(...)`. DiffEngine's half is done: the trios are labelled, and `Settle` clears only the running framework's. (left by the fix)
- [ ] A queued entry is still found by its line, with the member asked second. Three cases remain: a test that carries on past a failed verification and has two call sites only the line tells apart folds them into one; an entry the reporting framework has no content in is not moved, so a multi-target run can keep a stale duplicate; and a settle from the same member is believed on a key hit. Rebasing the hints of a file's remaining entries when one is accepted would close all three, and was not done because a batch accept finds entries by their `Variants` reference. (left by the fix)
- [ ] An `Append` with a stale hint takes the first call in the member that has no `Snapshot` call. Where an earlier call there is verified through files, or through `settings.Snapshot`, that is the wrong call, as it already was whenever such a call came first. (left by the fix)
- [ ] A `Remove` with a stale hint can answer AlreadyApplied while its anchored call is still there, because `RemovedAtHint` is asked first. (left by the fix: seen in its fuzzing, and present before it)

### Tray

- [ ] "Accept all in" a group from an attached viewer sends one `Accept` per key (`OwnerLink.AcceptGroup`), so the guard that leaves a delete whose file a move in the same sweep wrote does not apply there. (left by the fix)
- [ ] A delete held back that way stays in the menu with only a log line to say why, and a second "Accept all" carries it out. (left by the fix)
- [ ] An owning viewer's own batch looks to have the shape the tray's had: `ViewerSession.EnqueueTracked` replaces by key only, and `BeginAcceptAll` takes moves and deletes in queue order, so a delete can follow the move that wrote its file. (left by the fix: read, not run)
- [ ] A batch that begins between Verify raising a delete and queueing its patch can still carry out the delete without the patch. Closing that needs the two tied together on the wire. (left by the fix)
- [ ] A logoff also skips `TrayVersionFile.Delete()`, for the reason it skipped the staging. (left by the fix)
- [ ] The session ending was confirmed by sending the tray-shaped process `WM_QUERYENDSESSION` and `WM_ENDSESSION`, not by logging off. (left by the fix)

### Viewer, Linux head

- [ ] The body is not told about a taller footer. The model's eight chrome lines leave room for two rows of buttons and a status line; a paged document in a window under about 450 px wide needs four, and the last body rows are then hidden. Fixing it means the shim reporting fewer `rows`, which changes what `deview.h` says that field is. (left by the fix)
- [ ] The machine's fonts are drawn, not shaped: Arabic is unjoined and right to left text is in stored order. Colour emoji fonts and CFF2 variable fonts cannot be read by stb_truetype and are passed over, so a machine whose only CJK font is the variable Noto still shows replacement glyphs. At most fifteen fonts are merged. (left by the fix)
- [ ] Accept-all reads the Shift key's physical state, since raylib gives no modifiers with a character, so a latched Shift (sticky keys) is a plain accept. (left by the fix)

### Viewer, macOS head

- [ ] None of the four macOS fixes has been run. What would confirm each on a Mac:
  - Keys and clicks queued: with three entries queued, `pkill -STOP -x DiffEngineViewer`, press Down twice, `pkill -CONT`: the panes scroll two rows. Stopped, Tab then `a`: the second entry is the one accepted.
  - The scroller's knob: dragging it scrolls the panes while the button is down, and the scroller stays on the right edge through a resize.
  - The footer: `DiffEngineViewer --diff a.png b.png` at the default size has "images differ" on a line of its own with nothing over "Zoom in"; a PDF pair has two rows of buttons, all of which click.
  - Ligatures: `!= <= => -> == ...` in a text file are each drawn as separate characters.
- [ ] Turning ligatures off moves five OSX baselines, which can only be approved from the `received-macos-14` artifact of a CI run: `FileDiff`, `Images`, `ImagesEnlarged`, `Selection` and `Minimal`, each at the title's `<>`, and `Minimal` at the `...` of its folded rows too. If none of the five fails, the setting did not take. If all ten scenes fail, the copy is not the embedded font. (left by the fix)
- [ ] A live resize still draws the rows sliced for the old size until the mouse comes up. That half of the tracking loop bug needs a frame callback in the C ABI. A press in the scroller's slot followed by a drag is still AppKit's loop too. (left by the fix)
- [ ] The managed side still slices the body for a footer of one row. This head has 64 pt to spare, which is three rows of buttons or two and a status line; past that the last one or two body rows are not drawn. (left by the fix)
- [ ] Every auto-repeat of a held `a` or `d` is now handed over, including ones queued during a stall. `event.isARepeat` would drop them. The Windows item under Smaller is the same hazard. (left by the fix)
- [ ] The title row has the footer's old shape: the subtitle is drawn over a long title. (left by the fix)


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
