# Todo

What is open after the review of `main` at 991bc480 and the three rounds of fixes that followed it: bugs, performance, and smaller items. Everything those rounds fixed is gone from here. What is left is one item that was never fixed, the macOS viewer started with no console session, and what the fixes did not reach, by area.

An item with no tag was said by whoever made the fix it follows from. A tag says how else it is known. Nothing under the macOS head has been run by a person: the Swift cannot be built from Windows, and CI's `macos-14` job compiles it and runs the suite and the pixel captures, which open no window. The Linux head was built and run in an `ubuntu:24.04` container, under Xvfb with Mesa's software rasteriser.


## Library

- [ ] A viewer that is alive and never binds the port is still reported as launched after `BindWait`, and its payload file stays. That is the apphost's "install .NET" dialog, which does not exit. At the gate it cannot be told from a viewer that is only slow.
- [ ] A viewer that exits 1 or 4 has staged the patch itself, and the caller, told the launch failed, now stages it too: two trios in different `VerifyInline` folders until a passing run clears both.
- [ ] A failed launch still spends a `MaxInstance` slot, so after five in one process the cap answers instead.
- [ ] On macOS and Linux a tool started without ShellExecute still inherits the test host's streams. Nothing in the definitions tells a terminal tool, which needs them, from a windowed one: Neovim is declared `UseShellExecute: true` like the rest.
- [ ] `DiffRunner.LaunchProcess` still starts a third party tool in the test host's working directory, which then cannot be deleted while the tool is open. Left alone because a tool resolves relative arguments against it and `ProcessCleanup` matches on those same strings.
- [ ] None of the four tools started through `WindowsProcess.StartInheritingNothing` (Word and Excel comparers, Cursor, VS Code) was itself run. A console exe, a windowed exe and a `.cmd` stood in for them.
- [ ] The listener table is every connection the machine has, filtered, so reading it grows with them: 0.45 ms at 86 connections and 7.9 ms at 3,102. `ViewerClient` skips it for a port that answered in the last second; `PiperClient.PortIsHeld` reads it on every send. A listener-only table by P/Invoke would not grow.
- [ ] `RecheckUnownedAfter` is still ten minutes, though a recheck on Windows is a read of the table rather than two seconds, and could come down.
- [ ] Off Windows the connect is the only question asked, since a refusal there is immediate. Whether the table reads as empty under WSL1 was not checked.
- [ ] `PiperClient.PortIsHeld` takes any exception from the table as "may be held", where it took two kinds.
- [ ] Kept connections were not run on macOS: neither the rebind of a port whose last owner left a client holding a connection, nor the receive timeout surfacing as `SocketError.TimedOut`. And they were tested against a stand-in for an older owner, not a released tray or viewer.
- [ ] A client talking to an owner from before `keeps` still makes a connection per settle, so the port exhaustion stands against an older tray.
- [ ] The async inline send and every asking send (`TrySend` with a response, `InlineQueueClient`, `OwnerLink`'s five a second listing) are still a connection each.
- [ ] A connect that fails because the machine has no ports left is still recorded as an unowned port. Which `SocketError` Windows gives there was not established.
- [ ] Parallel settles take turns at the one connection under a lock, so a slow owner holds them in a line, where each used to wait on a connection of its own.
- [ ] A test process holds a socket to the queue's owner for its whole life. On .NET Framework sockets are inheritable, so a child a test starts without ShellExecute can hold that connection open after the test host exits. (not run)
- [ ] The accept wait is a fixed 100 ms from the second failure in a row. The case where a failure persists was run on Linux only, by using up the descriptors.
- [ ] Apple's `ps` was not run with `-ww`; its manual says a second `-w` uses as many columns as it needs.


## Inline snapshots

- [ ] Verify has to change for the staged trios of a multi-targeted project to be cleared per framework: in `InlineEngine.Settle()`, call `InlineStaging.Settle(MappedSourceFile, inline.Line, inline.MemberName, VerifierSettings.IntermediateDir, SnapshotInSource)` in place of `ClearStaged(...)`. DiffEngine's half is in: the trios are labelled, and `Settle` clears only the running framework's.
- [ ] Verify's half of a Remove applied twice: skipping the Remove when the source file is newer than the test assembly. DiffEngine's half keeps the removed call's line, empty, when a Snapshot call is under it, which is a rule about the next line and not a guarantee about line numbers.
- [ ] A queued entry is still found by its line, with the member asked second. Three cases remain: a test that carries on past a failed verification and has two call sites only the line tells apart folds them into one; an entry the reporting framework has no content in is not moved, so a multi-target run can keep a stale duplicate; and a settle from the same member is believed on a key hit. Rebasing the hints of a file's remaining entries when one is accepted would close all three, and was not done because a batch accept finds entries by their `Variants` reference.
- [ ] An `Append` with a stale hint takes the first call in the member that has no `Snapshot` call. Where an earlier call there is verified through files, or through `settings.Snapshot`, that is the wrong call, as it already was whenever such a call came first.
- [ ] A `Remove` with a stale hint can answer AlreadyApplied while its anchored call is still there, because `RemovedAtHint` is asked first. (seen in fuzzing, and present before the fix it was found beside)
- [ ] A `Remove` that takes a whole statement line (`settings.Snapshot("dup");`) still brings the next line up, so applied twice over a sibling `other.Snapshot("dup");` the second apply takes the sibling's statement. `RemovedAtHint` only recognises a removal under a verify call. (read, not run)
- [ ] An empty `TestName` or `MemberName` still reads back as null from `InlinePatchFile`; only `OriginalValue` has a line saying it is present and empty.
- [ ] The F# scanner outside a comment still takes a tick after an identifier character as part of the name, and still reads the holes of `$"..."` its own way. Only comments and backticked names were held to `dotnet fsi`.
- [ ] Lexing is still once a patch, in a batch as well: each patch is applied to what the one before it left, and one scan for all of them would not give the outcomes of applying in turn. 500 patches to a 600 KB file are 0.8 s of patching around one write.
- [ ] `CanAnchor` still reads and lexes the whole file for each call site a run has not seen before: 0.7 s for 500 call sites in the 600 KB file.
- [ ] A batch's one write that fails fails every patch from the first edit on, including one judged already applied or not found after it, and the file's mutex is held from the read to the write.
- [ ] A `VerifyInline` directory another process creates can be found up to a second late by `InlineStaging.Clear`.
- [ ] Two Windows-only tests assert that a send to a free port returns in under a second, where the refusal it avoids takes two.


## Tray

- [ ] "Accept all in" a group from an attached viewer sends one `Accept` per key (`OwnerLink.AcceptGroup`), so the guard that leaves a delete whose file a move in the same sweep wrote does not apply there.
- [ ] A delete held back that way stays in the menu with only a log line to say why, and a second "Accept all" carries it out.
- [ ] An owning viewer's own batch looks to have the shape the tray's had: `ViewerSession.EnqueueTracked` replaces by key only, and `BeginAcceptAll` takes moves and deletes in queue order, so a delete can follow the move that wrote its file. (read, not run)
- [ ] A batch that begins between Verify raising a delete and queueing its patch can still carry out the delete without the patch. Closing that needs the two tied together on the wire.
- [ ] A logoff also skips `TrayVersionFile.Delete()`, for the reason it skipped the staging.
- [ ] The session ending was confirmed by sending the tray-shaped process `WM_QUERYENDSESSION` and `WM_ENDSESSION`, not by logging off.
- [ ] A process id is matched to the payload's `Exe` by file name only, so a reused id now held by another copy of the same tool is still tracked, and killed on accept if the move can be killed.
- [ ] A tool started through a `.cmd` (VS Code, Rider) is never tracked as a process, so it never counts for "Accept all open" and is never closed by the tray. Nor is a tool whose launcher hands over to an executable of another name and exits. The tool definitions were not surveyed for either.
- [ ] "Discard (n)" still discards tracked moves on the UI thread, with up to 500 ms per killable tool waiting for it to exit. Only the queue's half is on a worker, and a bulk discard that fails is only logged.
- [ ] A scan that keeps failing is only in the log. Nothing tells the user.
- [ ] `ListingTag` can answer "unchanged" for a listing built while a move was briefly out of the dictionary during an accept and then restored.
- [ ] `Tracker.differing` is never pruned when a move leaves. (noticed, not touched)
- [ ] `KeyRegisterTests` registers a real global hot key, Ctrl+Alt+Shift+F24, for the length of each test, and fails where something else holds it.


## Viewer model

- [ ] Past its budget a diff is correct and may not be the smallest: two texts of more than 10,000 lines between them with 8,000 or more of the lines they share out of place. A block moved whole is found whatever its size. The same lines shuffled come out as nearly everything changed, where the longest run still in order could be kept: anchoring on the lines that occur once on each side, by the longest increasing run of them, would find it in the time of a sort.
- [ ] A pair is still read and diffed before the viewer answers the test process that sent it (`MessageHandler.TrackMove`). The diff is bounded, so what is left is two reads: a million lines a side, shuffled, is about two of the sender's three seconds. Answering first and filling the entry in afterwards, as a document arrives `Reading`, was not done.
- [ ] A screen is built when the state changes, which a scroll or a drag does every frame, and `QueueProjection.Rows` describes every row of the queue to draw the forty that fit: 0.55 ms and 1.1 MB at 2,000 entries, for each such frame. Slicing before describing would make it the visible rows'.
- [ ] `ScreenPayload` clips a row to the window's width in cells rather than the pane's, so about twice what a pane can show is encoded for the macOS and Linux heads: 3.6 ms a changed frame for a 4K window of 300 character CJK lines. `RowText.Shown`, which the WinForms head cuts with, would serve, once the model knows how many cells a pane has.
- [ ] A queue of more than a hundred pending files is looked at a hundred a pass, so a row that is not on screen follows its file within `count / 100` passes: two seconds for a thousand, and five times that while the window is hidden.
- [ ] Every arrival (`EnqueueInline`), settle and single accept still rebuilds the whole list from the whole queue, under the lock: a dictionary of the queue and an ordering. Only a batch's record step does not.
- [ ] The bulk discards are still one transition on the render thread: `DiscardGroup` and `DiscardAll` delete each received file under the lock.
- [ ] A snapshot discarded, or settled by a test that started passing, while its own source file is being written by a bulk accept was handed over with the rest of the file and is written with them. It is not counted, and a discard still takes it out of the queue. Closing it would take the applier asking, before its one write, which of the patches are still wanted.
- [ ] Both sides of a document are drawn at once, and four things about that could be better: a drawing is not stopped when the reader leaves its entry, though between two pages of a PDF it could be; the pages of a PDF that is put back because the other side stopped inside PDFium are dropped, and drawn again once PDFium is free; a PDF pair's right side waits for the left's first page, which is what lets the two be told apart when both stop; and `Withdrawn`, which takes a rendering back out of the state, lives in `DocumentWatch` where it belongs beside `ViewerSession.Rendered`.
- [ ] Which of two PDFs stopped inside PDFium is inferred from whose pages stopped first, not known. A thread descheduled between landing a page and asking for the lock, at the moment the other side hangs, would have the innocent side given up on and the culprit put back.
- [ ] `TrackedWatch` closes an open context menu when a file is written again with what it held, since the entry is replaced by its restamped copy through `ViewerSession.Refresh`. The reader is not moved.
- [ ] The two tests that show a pair is not diffed again, and the one that bounds what reading a file allocates, were not run against the code from before their fixes.


## Viewer, Windows head

- [ ] Between half its own size and its own size an enlarged picture is still scaled on every paint: 15 ms for a 4000 by 3000 pair at 400%. A copy there would cost up to the decoded picture again, 96 MB for that pair. Decoding premultiplied (`Format32bppPArgb` in `ImageCache.Load`) measured 9 ms, and was left out because it moves translucent pixels by one level in five pixel scenes.
- [ ] A picture drawn from its scaled copy sits on whole pixels, up to half a pixel from its exact placement, so two pictures of different sizes can be a pixel apart relative to each other while zoomed below half size.
- [ ] `Uncomposable` is a picture's rather than a size's, so a scale that failed also stops the fitted copy being made again at a new size.
- [ ] `RowText.Shown`'s tests are in `DiffEngineViewer.Windows.Tests`, beside its one caller. They belong beside `CellGridTests`.
- [ ] A minimised window slows only its own frame wait. `OwnerLink`, `TrackedWatch` and the document reader go by `Hidden`, which only a Hide command sets, so they keep their on screen cadence while minimised. The head would have to tell the loop.
- [ ] A status that does not fit loses what is past two lines, behind an ellipsis and a tooltip. The footer does not wrap its buttons or give the status a line of its own as the other two heads do. Whether a scaled display shows the middle of a wrapped status is unchecked: captures are unscaled.
- [ ] Nothing proves by throwing that an exception comes out of a message pump in the test hosts rather than WinForms' dialog. The test reads the mode back instead, through an internal of WinForms, because a throwing one is what put the dialog on screen.
- [ ] `TwoKeysInOnePumpAreTwoCommands`, `ATextEntryHasNoPictureForTheWheelToFind` and `TheWheelOverAPictureZoomsAndOverTheRowsScrolls` failed once in a run of the whole solution and passed in the next and twice alone. They post keys and wheel turns to a form, and someone was using the machine. (seen once)
- [ ] Footer buttons leave `UseMnemonic` on, so an ampersand in a label would become an Alt access key. (noticed, not looked into)


## Viewer, Linux head

- [ ] The body is not told about a taller footer. The model's eight chrome lines leave room for two rows of buttons and a status line; a paged document in a window under about 450 px wide needs four, and the last body rows are then hidden. Fixing it means the shim reporting fewer `rows`, which changes what `deview.h` says that field is.
- [ ] The machine's fonts are drawn, not shaped: Arabic is unjoined and right to left text is in stored order. Colour emoji fonts and CFF2 variable fonts cannot be read by stb_truetype and are passed over, so a machine whose only CJK font is the variable Noto still shows replacement glyphs. At most fifteen fonts are merged.
- [ ] Accept-all reads the Shift key's physical state, so a latched Shift (sticky keys) is a plain accept.
- [ ] A held letter repeats its command at the keyboard's repeat rate, so holding `a` accepts repeatedly. Only navigation should repeat, as on Windows.
- [ ] An idle window still turns sixty times a second. Each turn compares the screen's bytes with the last one's, asks after the files behind the pictures on it and waits out its sixtieth: 3 to 9 ms of processor a second. Waiting on the window system instead would take the managed loop, which also asks after its owner and its files each turn, being told when to wake.
- [ ] Nothing in `DiffEngineViewer.Tests` fails if the window goes back to drawing every frame. `NativeIdleBenchmarks` shows it, in its Drawn column, and is run by hand in the container.
- [ ] A hidden window is still built and drawn when its screen changes, which an arrival in the queue does.
- [ ] Leaving an idle window alone was run only under Xvfb with Mesa's software rasteriser, with no window manager and under openbox. Not on a GPU, under a compositor, on Wayland or over forwarded X, where what the window system keeps of a window that is not being drawn may differ, and where leaving one alone matters most.
- [ ] `deview_capture` makes its ImGui context without `ImGuiBackendFlags_RendererHasVtxOffset`, which the window's declares, so a capture whose draw list passes 65,535 vertices comes out scrambled. Nothing captures at that size, but dense text at 4K could. (ran, with the flag added to an older shim)
- [ ] A picture larger than `GL_MAX_TEXTURE_SIZE` draws as nothing. It could be reduced on the decoder thread to fit a texture. `PixelTests.ImageTooLargeForATexture` holds only under llvmpipe, whose limit is 16384.
- [ ] Display scale is read once, as the window is made. A change of `Xft.dpi` while running, and a scale per monitor under Wayland or XWayland, are not followed, and a placement remembered at one scale is used in pixels at another. Checked only under Xvfb with an X resource at 144 and 192, never on a HiDPI desktop.
- [ ] A frame of two pictures costs about a tenth more under llvmpipe since mipmaps (opaque at 4K, 27.0 ms to 30.1), probably the trilinear sampling of a picture drawn under half size. Mipmaps also add about a third to a picture's memory, drawn small or not. (measured, cause unconfirmed)
- [ ] `[`, `]`, `0`, `-` and `=` have no fallback by position on a layout that is not Latin; only the letters do.
- [ ] All input comes through GLFW's callbacks. A release that never arrives would leave a button held: GLFW's own release on losing focus is relied on, and hiding the window during a press was not tried. A two finger tap on a real touchpad is unconfirmed.


## Viewer, macOS head

- [ ] `Runtime.open` cannot fail (it always returns true), so with no console session, as over SSH, AppKit aborts the process after the port was bound and the patch is lost. `CGSessionCopyCurrentDictionary()` is the documented question to ask, and was not used because it may also answer "no session" over SSH while the same user is logged in at the console, where the viewer opens today. What would settle it: print that call from an SSH shell with and without a console login, and run the viewer in each. (reported: plausible)
- [ ] To confirm on a Mac, from the bug fixes. Two are event handling that no capture exercises:
  - Keys and clicks queued: with three entries queued, `pkill -STOP -x DiffEngineViewer`, press Down twice, `pkill -CONT`: the panes scroll two rows. Stopped, Tab then `a`: the second entry is the one accepted.
  - The scroller's knob: dragging it scrolls the panes while the button is down, and the scroller stays on the right edge through a resize.
  - The footer: `DiffEngineViewer --diff a.png b.png` at the default size has "images differ" on a line of its own with nothing over "Zoom in"; a PDF pair has two rows of buttons, all of which click.
  - Ligatures: `!= <= => -> == ...` in a text file are each drawn as separate characters.
- [ ] To confirm on a Mac, from the performance changes. Nothing on CI runs the clip test or the scaled copy:
  - The premise: break in `Renderer.draw` while a spinner turns and print `context.boundingBoxOfClipPath`. The spinner's 44 pt square, or the whole view.
  - A turn: Time Profiler on a pair with a long PDF. No `CTLineCreateWithAttributedString` under `Renderer.draw` for a turn, whichever clip AppKit hands over.
  - Text outside Latin: `--diff` two files of Chinese and hold Down. The same symbol for each frame: the new row alone.
  - An enlarged picture: `--diff` two 2880 by 1800 screenshots, `+` once, and drag. Time under `Renderer.enlarged` for each frame: a blit, and one 1560 by 975 bitmap a pane about a tenth of a second after the step.
- [ ] To confirm on a Mac, from the smaller fixes: the six changes that redraw a window when a picture lands in a partial draw, match the page keys by the character typed, leave the other pane's pan alone, stop redrawing a window with no room for its picture, take a wheel's click and a control-click, and return from the pump on input. Five are event handling or window state that no capture exercises. The check for each is in its commit's message.
- [ ] A live resize still draws the rows sliced for the old size until the mouse comes up. It needs a frame callback in the C ABI. A press in the scroller's slot followed by a drag is still AppKit's loop too.
- [ ] The managed side still slices the body for a footer of one row. This head has 64 pt to spare, which is three rows of buttons or two and a status line; past that the last one or two body rows are not drawn.
- [ ] Every auto-repeat of a held `a` or `d` is handed over, including ones queued during a stall. `event.isARepeat` would drop them, as the Windows head does.
- [ ] The title row has the footer's old shape: the subtitle is drawn over a long title.
- [ ] Since macOS 11 a view with an automatic backing store is handed its whole bounds whatever was invalidated, clip included, so the clip test probably leaves nothing out on any supported macOS. Two routes would make it pay: `layer.contentsFormat = .RGBA8Uint` in `viewWillDraw`, which changes how the whole window is stored, or a view of the spinner's own. (read, in Apple's developer forums)
- [ ] A byte-equal pair of documents names one page's png in both panes. With one scaled copy a picture and panes a point apart in width, `fitted` looks to make the copy again for each pane in turn without end. The enlarged path stays out of it by drawing such a pair from the picture. (read, not run)
- [ ] A drag still pulls the other pane's centre into the dragged pane's range on an axis both can move on, when the two pictures differ in shape. Only the axis the dragged pane cannot move on is left alone. The Linux head has the same rule.
- [ ] A hidden, miniaturised or covered window comes forward up to a tenth of a second late for a patch arriving over the socket, since the managed side cannot interrupt the pump, and `OwnerLink` and `TrackedWatch` do not slow for a window that is only covered or miniaturised. Both need the ABI to carry it. During a scroll or a drag the loop turns at the rate events arrive, which a 120 Hz device makes faster than sixty.
- [ ] `+`, `-` and `=` are still matched on `charactersIgnoringModifiers`; only the brackets are matched on `characters`.
