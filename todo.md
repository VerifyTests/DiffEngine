# Review todo

Findings from a review of `main` at 991bc480 (2026-10-03). The list from the review at 4244ebe6 is closed, so this one weights what has landed since: documents and maps, the text diff, zoom and pan, the remembered window and views, and pictures decoded off the UI thread.

Most of it came from six reviews run alongside, one per area: the library, the inline patcher, the tray, and the Windows, Linux and macOS heads. Every bug found is fixed and gone from this list: the five in the viewer's model, and the twenty seven the six reviews found. So is every performance item, all fourteen. What is left is what those fixes did not reach, and the smaller ones.

File and line references are as of a01dfc1d, before the second round of fixes and before the performance ones. The code they point at is unchanged, but lines below an edit have moved, so go by the names.

- **reproduced**: run and seen by me. Either the API involved, or the repo's own sources compiled into a scratch console project outside the repo. No test in the repo yet.
- **measured**: timed by me in that same project. Release, net10.0, this machine.
- **read**: confirmed by me reading the code path, not run.
- **reported**: found by one of the six reviews and not rerun by me. What follows the word is the reviewer's own evidence: *ran* is a probe of theirs outside the repo, *measured* a timing of theirs, *read* a trace through the code, and *plausible* a reading that rests on something they could not run, with what would settle it. For each of these I checked that the code it quotes is in the tree as quoted, and nothing more.
- **left by the fix**: said by whoever fixed the bug it sits under, about the part the fix did not reach.
- Nothing under a macOS heading has been run by a person: the Swift cannot be built from Windows. CI's `macos-14` job compiles it and runs the suite and the pixel snapshots, and it passes there. The Linux fixes were built and run in an `ubuntu:24.04` container, and pass on CI's Linux job too. The two macOS performance changes are in the same position as the fixes, and have not been measured either.


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

- [ ] The four macOS fixes compile and the suite passes on CI, but none has been run by a person, and two are event handling that no capture exercises. What would confirm each on a Mac:
  - Keys and clicks queued: with three entries queued, `pkill -STOP -x DiffEngineViewer`, press Down twice, `pkill -CONT`: the panes scroll two rows. Stopped, Tab then `a`: the second entry is the one accepted.
  - The scroller's knob: dragging it scrolls the panes while the button is down, and the scroller stays on the right edge through a resize.
  - The footer: `DiffEngineViewer --diff a.png b.png` at the default size has "images differ" on a line of its own with nothing over "Zoom in"; a PDF pair has two rows of buttons, all of which click.
  - Ligatures: `!= <= => -> == ...` in a text file are each drawn as separate characters.
- [ ] A live resize still draws the rows sliced for the old size until the mouse comes up. That half of the tracking loop bug needs a frame callback in the C ABI. A press in the scroller's slot followed by a drag is still AppKit's loop too. (left by the fix)
- [ ] The managed side still slices the body for a footer of one row. This head has 64 pt to spare, which is three rows of buttons or two and a status line; past that the last one or two body rows are not drawn. (left by the fix)
- [ ] Every auto-repeat of a held `a` or `d` is now handed over, including ones queued during a stall. `event.isARepeat` would drop them. The Windows item under Smaller is the same hazard. (left by the fix)
- [ ] The title row has the footer's old shape: the subtitle is drawn over a long title. (left by the fix)


## Performance

Nothing that was found as a performance item is open. Each was measured before and after by a benchmark that is now in the repository, in `src/DiffEngine.Benchmarks`, `src/DiffEngineViewer.Benchmarks` and `src/DiffEngineViewer.Windows.Benchmarks`, and the numbers are in the commits that made the changes. The two Linux items were measured in the `ubuntu:24.04` container, by `NativeFrameBenchmarks` and `NativeIdleBenchmarks`, which are left out of a run anywhere else. Two were not measured, because nothing here can run them: both macOS items. What follows is what the fixes left.

### Viewer model

- [ ] Past its budget a diff is correct and may not be the smallest: two texts of more than 10,000 lines between them with 8,000 or more of the lines they share out of place. A block moved whole is found whatever its size. The same lines shuffled come out as nearly everything changed, where the longest run still in order could be kept: anchoring on the lines that occur once on each side, by the longest increasing run of them, would find it in the time of a sort. (left by the fix)
- [ ] A pair is still read and diffed before the viewer answers the test process that sent it (`MessageHandler.TrackMove`). The diff is bounded now, so what is left is two reads: a million lines a side, shuffled, is about two of the sender's three seconds. Answering first and filling the entry in afterwards, as a document arrives `Reading`, was not done. (left by the fix)
- [ ] A screen is built when the state changes, which a scroll or a drag does every frame, and `QueueProjection.Rows` describes every row of the queue to draw the forty that fit: 0.55 ms and 1.1 MB at 2,000 entries, for each such frame. Slicing before describing would make it the visible rows'. (left by the fix)
- [ ] `ScreenPayload` clips a row to the window's width in cells rather than the pane's, so about twice what a pane can show is encoded for the macOS and Linux heads: 3.6 ms a changed frame for a 4K window of 300 character CJK lines. `RowText.Shown`, which the WinForms head now cuts with, would serve, once the model knows how many cells a pane has. (left by the fix)
- [ ] A queue of more than a hundred pending files is looked at a hundred a pass, so a row that is not on screen follows its file within `count / 100` passes: two seconds for a thousand, and five times that while the window is hidden. The entry on screen is still looked at every pass. (left by the fix)
- [ ] Only a batch's record step stopped rebuilding the whole list from the whole queue. Every arrival (`EnqueueInline`), settle and single accept still does, under the lock: a dictionary of the queue, two orderings and a key an entry. The two `Smaller` items on `QueueProjection.Order` and `PendingInline.Key` are parts of it. (left by the fix)
- [ ] The bulk discards are still one transition on the render thread: `DiscardGroup` and `DiscardAll` delete each received file under the lock. A discard waits on nothing, so they were left. (left by the fix)
- [ ] A snapshot discarded, or settled by a test that started passing, while its own source file is being written by a bulk accept was handed over with the rest of the file and is written with them. It is not counted, and a discard still takes it out of the queue. Before, that moment was the snapshot's own apply rather than its file's. Closing it would take the applier asking, before its one write, which of the patches are still wanted. (left by the fix)
- [ ] Both sides of a document are drawn at once, and four things about that are as they are for a reason and could be better: a drawing is not stopped when the reader leaves its entry, though between two pages of a PDF it now could be; the pages of a PDF that is put back because the other side stopped inside PDFium are dropped, and drawn again once PDFium is free; a PDF pair's right side waits for the left's first page, which is what lets the two be told apart when both stop; and `Withdrawn`, which takes a rendering back out of the state, lives in `DocumentWatch` where it belongs beside `ViewerSession.Rendered`. (left by the fix)
- [ ] Which of two PDFs stopped inside PDFium is inferred from whose pages stopped first, not known. A thread descheduled between landing a page and asking for the lock, at the moment the other side hangs, would have the innocent side given up on and the culprit put back. (left by the fix)

### Library

- [ ] The listener table is every connection the machine has, filtered, so reading it grows with them: 0.45 ms at 86 connections and 7.9 ms at 3,102. `ViewerClient` skips it for a port that answered in the last second; `PiperClient.PortIsHeld` reads it on every send, as it did before. A listener-only table by P/Invoke would not grow. (left by the fix)
- [ ] `RecheckUnownedAfter` is still ten minutes, though a recheck on Windows is now a read of the table rather than two seconds, and could come down. (left by the fix)
- [ ] Off Windows the connect is still the only question asked, since a refusal there is immediate. Whether the table reads as empty under WSL1 was not checked. (left by the fix)
- [ ] `PiperClient.PortIsHeld` now takes any exception from the table as "may be held", where it took two kinds. (left by the fix)

### Inline snapshots

- [ ] Lexing is still once a patch, in a batch as well: each patch is applied to what the one before it left, and one scan for all of them would not give the outcomes of applying in turn. 500 patches to a 600 KB file are 0.8 s of patching around one write. (left by the fix)
- [ ] `CanAnchor` still reads and lexes the whole file for each call site a run has not seen before: 0.7 s for 500 call sites in the 600 KB file. (left by the fix)
- [ ] A batch's one write that fails fails every patch from the first edit on, including one judged already applied or not found after it, and the file's mutex is held from the read to the write. (left by the fix)
- [ ] A `VerifyInline` directory another process creates can be found up to a second late by `InlineStaging.Clear`. (left by the fix)
- [ ] Two Windows-only tests assert that a send to a free port returns in under a second, where the refusal it avoids takes two. (left by the fix)

### Viewer, Windows head

- [ ] Between half its own size and its own size an enlarged picture is still scaled on every paint: 15 ms for a 4000 by 3000 pair at 400%. A copy there would cost up to the decoded picture again, 96 MB for that pair. Decoding premultiplied (`Format32bppPArgb` in `ImageCache.Load`) measured 9 ms, and was left out because it moves translucent pixels by one level in five pixel scenes. (left by the fix)
- [ ] A picture drawn from its scaled copy sits on whole pixels, up to half a pixel from its exact placement, so two pictures of different sizes can be a pixel apart relative to each other while zoomed below half size. (left by the fix)
- [ ] `Uncomposable` is a picture's rather than a size's, so a scale that failed also stops the fitted copy being made again at a new size. (left by the fix)
- [ ] `RowText.Shown`'s tests are in `DiffEngineViewer.Windows.Tests`, beside its one caller. They belong beside `CellGridTests`. (left by the fix)
- [ ] One of the fixes changes what is drawn. A character of two UTF-16 units whose first column of pixels is its pane's last, an emoji at the very edge, used to be cut to nothing and is now drawn. (left by the fix)

### Viewer, Linux head

- [ ] An idle window still turns sixty times a second. Each turn compares the screen's bytes with the last one's, asks after the files behind the pictures on it and waits out its sixtieth: 3 to 9 ms of processor a second, where it was half a second to ten. Waiting on the window system instead would take the managed loop, which also asks after its owner and its files each turn, being told when to wake. (left by the fix)
- [ ] What is not built rests on the list of what a frame is built from being whole: the screen, the pointer, the keys, the window, the decoder, the font finder, a tooltip's delay and the files behind the pictures. Anything `BuildFrame` comes to read that is none of those has to be asked in `deview_present` before a window is left alone, or the window shows the frame before until something else arrives. A frame that is built and comes out the same is not drawn whatever it read, so that half needs no such care. (left by the fix)
- [ ] Nothing in `DiffEngineViewer.Tests` fails if the window goes back to drawing every frame. `NativeIdleBenchmarks` shows it, in its Drawn column, and is run by hand in the container. (left by the fix)
- [ ] A hidden window is still built and drawn when its screen changes, which an arrival in the queue does. (left by the fix)
- [ ] Run only under Xvfb with Mesa's software rasteriser, with no window manager and under openbox. Not on a GPU, under a compositor, on Wayland or over forwarded X, where what the window system keeps of a window that is not being drawn may differ, and where leaving one alone matters most. (left by the fix)
- [ ] `deview_capture` makes its ImGui context without `ImGuiBackendFlags_RendererHasVtxOffset`, which the window's declares, so a capture whose draw list passes 65,535 vertices comes out scrambled. The old checkerboard took a 4K capture of two large pictures past it, which is how it was found. Nothing captures at that size, and the checkerboard no longer takes a capture there, but dense text could. (left by the fix: ran, with the flag added to the shim from before the fix)

### Viewer, macOS head

- [ ] Neither macOS change has been compiled by a person or run at all. CI's `macos-14` job compiles them, and its captures draw text from the kept lines. Nothing there runs the clip test or the scaled copy. What would confirm each on a Mac:
  - The premise: break in `Renderer.draw` while a spinner turns and print `context.boundingBoxOfClipPath`. The spinner's 44 pt square, or the whole view.
  - A turn: Time Profiler on a pair with a long PDF. `CTLineCreateWithAttributedString` under `Renderer.draw` for each turn: about 150 before, none after, whichever clip AppKit hands over.
  - Text outside Latin: `--diff` two files of Chinese and hold Down. The same symbol for each frame: every character before, the new row alone after.
  - An enlarged picture: `--diff` two 2880 by 1800 screenshots, `+` once, and drag. Time under `Renderer.enlarged` for each frame: a `.high` resample of both panes before, a blit after, and one 1560 by 975 bitmap a pane about a tenth of a second after the step.
- [ ] Since macOS 11 a view with an automatic backing store is handed its whole bounds whatever was invalidated, clip included, so the clip test is safe and probably leaves nothing out on any supported macOS. What makes a spinner's turn cheap there is the kept lines. Two routes would make the clip test pay: `layer.contentsFormat = .RGBA8Uint` in `viewWillDraw`, which changes how the whole window is stored, or a view of the spinner's own. (left by the fix: read, in Apple's developer forums)
- [ ] A byte-equal pair of documents names one page's png in both panes. With one scaled copy a picture and panes a point apart in width, `fitted` looks to make the copy again for each pane in turn without end. The enlarged path stays out of it by drawing such a pair from the picture. (left by the fix: read, not run)


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

- [ ] Input is sampled as state once a frame, so a press and release that arrive together are not seen and several wheel events collapse into one: `deview.cpp:909-923`, `:2217-2220`. Sent together by xdotool under Xvfb, none of ten clicks was seen and three of ten key presses were. A touchpad two finger tap would not open a context menu, which a tap on a real touchpad would confirm. Fix: chain GLFW's mouse button and scroll callbacks and feed ImGui from them. (reported: plausible, and since run in the container)
- [ ] Ctrl+A and Ctrl+C are still by US key position, arrows and paging do not repeat when held, and no letter shortcut matches on a non-Latin layout: `deview.cpp:929-944`, `:976-985`. Fix: `IsKeyPressedRepeat` for navigation, and resolve the chords through `GetKeyName`. (reported: read)
- [ ] No display scale handling: no `FLAG_WINDOW_HIGHDPI` and no `GetWindowScaleDPI()`, so on a HiDPI X11 display everything is about half size (`deview.cpp:2075-2091`, `:2137`). What would settle it: a display with `Xft.dpi` 192. (reported: plausible)
- [ ] A queue row's context menu is not kept inside the window: the clamp at `deview.cpp:1929-1944` is inside `if (paneMenu)`, so the last row's menu is cut off at the default size. (reported: read)
- [ ] Hover never ends when the pointer leaves the window: `io.AddMousePosEvent(mouse.x, mouse.y)` is unconditional (`deview.cpp:915-916`), so a row stays highlighted and its tooltip appears with the pointer elsewhere. (reported: plausible)
- [ ] A picture larger than `GL_MAX_TEXTURE_SIZE` draws as a black box rather than as nothing (`deview.cpp:613-621`, `:706-712`), and pictures shrunk more than two times are sampled bilinear with no mipmaps (`:464-469`, `:1457-1464`), so thin lines and small text drop out. (reported: plausible, and read)
- [ ] Labels containing `##` are cut short, since ImGui hides everything from there on: pane headers, queue rows, menu items and buttons (`deview.cpp:1687-1688`, `:1716`, `:1738`, `:1961`, `:2003`). (reported: read)

### Viewer, macOS head

- [ ] A picture landing during a repaint of the spinner alone is drawn clipped to the spinner's rectangle and never completed: `Renderer.swift:245`, `Runtime.swift:223-239`. A few in a thousand large images. The scaled copy of an enlarged picture lands the same way, and one that lands then leaves the picture drawn at `.low`. Fix: have `draw` report that something landed, and turn that into a full redraw. (reported: read)
- [ ] `[` and `]` never match on layouts where they need Option, since `charactersIgnoringModifiers` yields the digit (`ViewerView.swift:412-433`): German, French, Nordic, Spanish and Italian layouts cannot turn pages by key. Fix: match symbols on `event.characters` first. (reported: read)
- [ ] A pan drag in a pane that cannot move on an axis resets that axis for the other pane: the report is clamped with the dragged pane's own extents (`Renderer.swift:164-174`, `ViewerView.swift:180-187`, and `deview.cpp:1587-1591` on Linux). Fix: report the frame's own centre unchanged on an axis the pane cannot move on. (reported: read)
- [ ] `picturesChanged` never settles when a picture has no room, so a window shorter than about 176 pt redraws at sixty frames a second (`Renderer.swift:485-489`, `:846-848`). Fix: a `contentMinSize`, or record the stamp when nothing is drawn. (reported: read)
- [ ] A notched mouse wheel may do nothing until ten slow clicks add up (`ViewerView.swift:310-322`), and control-click never opens a context menu (`:124-170`). What would settle them: a Mac with a wheel mouse. (reported: plausible)
- [ ] The pump waits out its full 16.7 ms after input, and nothing slows the loop when the window is occluded or miniaturised (`Runtime.swift:209-249`, `:257-276`). (reported: read)
- [ ] `Runtime.open` cannot fail (`Runtime.swift:57-91` always returns true), so with no console session, as over SSH, AppKit aborts the process after the port was bound and the patch is lost. What would settle it: a failing inline snapshot over SSH with nobody logged in. (reported: plausible)
