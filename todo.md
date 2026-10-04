# Todo

What is open after the review of `main` at 991bc480 and the rounds of fixes that followed it. Everything those rounds fixed is gone from here. What is left is what the fixes did not reach, by area, and a few items that were looked at and left for a reason the item gives.

An item with no tag was said by whoever made the fix it follows from. A tag says how else it is known. Nothing under the macOS head has been run by a person: the Swift cannot be built from Windows, and CI's `macos-14` job compiles it and runs the suite and the pixel captures, which open no window. The Linux head was built and run in an `ubuntu:24.04` container, under Xvfb with Mesa's software rasteriser.


## Library

- [ ] A viewer that is alive and never binds the port is still reported as launched after `BindWait`, and its payload file stays. That is the apphost's "install .NET" dialog, which does not exit. At the gate it cannot be told from a viewer that is only slow.
- [ ] A viewer that exits 1 or 4 has tried to stage the patch itself, and the caller, told the launch failed, stages it too: two trios in different `VerifyInline` folders until a passing run clears both. The gate cannot tell staged from tried, because the viewer discards `InlineStaging.Persist`'s count: it needs an exit code of its own for "staged", from `ViewerProgram`.
- [ ] On macOS and Linux a tool started without ShellExecute still inherits the test host's streams. Nothing in the definitions tells a terminal tool, which needs them, from a windowed one: Neovim is declared `UseShellExecute: true` like the rest.
- [ ] `DiffRunner.LaunchProcess` still starts a third party tool in the test host's working directory, which then cannot be deleted while the tool is open. Left alone because a tool resolves relative arguments against it and `ProcessCleanup` matches on those same strings.
- [ ] None of the four tools started through `WindowsProcess.StartInheritingNothing` (Word and Excel comparers, Cursor, VS Code) was itself run. A console exe, a windowed exe and a `.cmd` stood in for them.
- [ ] The table of Windows' listeners still grows a little with the machine's connections, 0.13 ms to 0.32 ms across 3,000 rows, and `PiperClient.PortIsHeld` reads it on every send. `GetExtendedTcpTable` was run on Windows 11 x64 only: not 32-bit, not ARM64, not with IPv6 disabled.
- [ ] A port the table found empty stands as unowned for a second; one a connect found empty still stands for ten minutes, which is every platform but Windows. Whether the table reads as empty under WSL1 was not checked.
- [ ] Only a refusal or an unanswered connect is remembered as nobody being there. A platform that reports "nothing listening" some other way would pay the connect on every telling send. Which `SocketError` Windows gives when it is out of ports was not established, and no longer matters to the memory.
- [ ] The two kept connections, for telling sends and for listings, were not run on macOS, and were tested against a stand-in for an older owner, not a released tray or viewer. A listing that times out drops its kept connection and the next opens it again: the tray's and the attached viewer's callers were not read for what they assume about that.
- [ ] A client talking to an owner from before `keeps` still makes a connection per settle, so the port exhaustion stands against an older tray.
- [ ] Sends that change the queue (`Accept`, `Discard`, `AcceptAll`, `Inline`, `Diff`, `Focus`) and the async send are still a connection each. One written to a kept connection whose owner has just gone would be sent again, with nothing to say whether it was acted on.
- [ ] Parallel settles take turns at the one connection under a lock, so a slow owner holds them in a line, where each used to wait on a connection of its own.
- [ ] On .NET Framework only the kept connections are kept from child processes. A child started in the moment a one-off exchange is open still inherits that socket. (not run)
- [ ] The accept wait is a fixed 100 ms from the second failure in a row. The case where a failure persists was run on Linux only, by using up the descriptors.
- [ ] Apple's `ps` was not run with `-ww`; its manual says a second `-w` uses as many columns as it needs.


## Inline snapshots

- [ ] Verify has to change for the staged trios of a multi-targeted project to be cleared per framework: in `InlineEngine.Settle()`, call `InlineStaging.Settle(MappedSourceFile, inline.Line, inline.MemberName, VerifierSettings.IntermediateDir, SnapshotInSource)` in place of `ClearStaged(...)`. DiffEngine's half is in: the trios are labelled, and `Settle` clears only the running framework's.
- [ ] A `Remove` with a stale hint can answer AlreadyApplied while its anchored call is still there. The source cannot tell that from the same Remove applied twice over a sibling with the same literal, so it needs Verify's half, skipping the Remove when the source file is newer than the test assembly, or a Remove that never changes line numbers. An empty line over a Snapshot call is read as removed, which is one more shape of it. (reproduced)
- [ ] An owner now takes a file's pending entries to the lines an accept moved their call sites to (`InlineQueue.Rebased`), and a batch asks each patch about its moved line. An edit nothing reports still leaves the lines wrong until a run reports them again: one made by hand, and an accept applied by another process, the IDE plugin among them, whose `SettleAppliedInline` carries no lines. For those a queued entry is still found by its line with the member asked second, and the three cases stand: two call sites only the line tells apart are folded into one, an entry the reporting framework has no content in is not moved, and a settle from the same member is believed on a key hit. The wire would have to carry which lines moved.
- [ ] A run that was under way when a snapshot was accepted reports its later call sites by the lines they had. Its patch is then looked for under a key the owner has moved on from, and where another entry of the same member has been taken to that very line, it is folded into that one. Before, the stale lines were the queue's and it was the run after the accept that missed. (reasoned, not run)
- [ ] Which lines an edit moved is read off the source before and after, by what the two begin and end with alike. An edit that inserts text beginning as the text after it does, across a line break, is counted from a line too far down. Nothing the patcher writes was found to do that.
- [ ] A menu or a window showing an owner's queue names an entry by a key that the owner changes when it accepts another snapshot of the file. Until the next listing, a click there is answered as an entry that has gone, or, after a snapshot that got shorter, can name the entry that came up onto that line. An attached viewer follows its selection across the move by call site (`IndexOfMoved`), which two call sites alike in everything but the line defeat.
- [ ] An `Append` whose line was already wrong when the batch began, with more than one call in its member, is still refused: only the moves the batch itself made are brought along.
- [ ] A `Remove` of a statement whose receiver is on a line above the call still brings the lines under it up.
- [ ] A batch still copies the whole source for each patch that edits: 1.2 MB a patch in a 600 KB file, 0.29 s for 500.
- [ ] A dry run is answered from a kept scan while the file's length and write time stand. A rewrite of the same length that puts the old write time back is not seen, and up to four files' source and maps are held for the life of the process. `ASecondProbeOfAFileDoesNotReadItAgain` relies on a second open being refused, which was not run off Windows.
- [ ] The scan carried from one edit to the next restarts at the edit's line, never at a line that follows a backslash. A lexer change that looks across a line break some other way has to extend `SourceScan.RestartFor`.
- [ ] A `VerifyInline` directory another process creates can be found up to a second late by `InlineStaging.Clear`.


## Tray

- [ ] The hold on a delete is let go when the delete is raised again, which another framework's process of the same run, having decided before the move was accepted, can do. True of the tray's tracker and of an owning viewer's queue (`ViewerSession.EnqueueTracked`) alike.
- [ ] A tray's listing taken while a move is out of `moves` being accepted, before `MarkWritten`, says the delete on its target is not held. A group accept sent from an attached viewer inside that one poll interval still sends the delete's key, after the move has written the file. `Tracker.HeldReason` does not know of a move in flight. (read, not run)
- [ ] An owning viewer's delete kept by a batch because a move was still pending keeps that status text if the move is later discarded. `ViewerSession.HeldReason`, the listing and the next batch are right; only the row's tooltip is stale.
- [ ] A held delete's row carries the same ` !` mark as a failed entry in every renderer, on an owning and an attached viewer. Only the tooltip tells them apart.
- [ ] A move arriving in an owning viewer withdraws the delete pending on its target, which can be the entry on screen, and closes an open menu as any removal does. The withdrawal and the mark compare paths as the file system does (`InlineKey.SamePath`), case sensitive on Linux, where the tray's rule ignores case throughout. Not run off Windows.
- [ ] A batch that begins between Verify raising a delete and queueing its patch can still carry out the delete without the patch. Closing that needs the two tied together on the wire.
- [ ] The session ending, which stages the queue and removes the version marker, was exercised by sending `WM_QUERYENDSESSION` and `WM_ENDSESSION` to the window, not by logging off, and its wiring in `Program.Inner` is read, not run.
- [ ] A process is believed to be a move's tool only when its command line names the received file. A custom tool that rewrites the path it is given is not tracked as a process: not closed on accept, not counted as open. The command line is read by a call Windows has from 8.1, and was read from PowerShell children only, not a real diff tool.
- [ ] A tool started through a script is never tracked as a process: VS Code always (`code.cmd`), and Rider when it is found on the PATH as `rider.cmd`. Nor is one whose launcher hands over and exits: Araxis, Sublime Merge, Cursor, Meld and an already running Rider, from knowledge of the tools and not from running them. None can be tracked safely from the tray, since the window lives in a process whose command line does not name the pair.
- [ ] "Discard" on a single move still ends its tool on the UI thread, up to 500 ms. A move whose received file could not be deleted in a bulk discard is untracked with only a log line.
- [ ] A scan that keeps failing still writes a log line every two seconds.
- [ ] `KeyNameTests` still builds two registers with the constructor that registers with Windows. Both bind only a key name that does not parse, so nothing reaches `RegisterHotKey` today.
- [ ] `MenuBuilderTest` opens its menus with `TopLevel` false, which Windows reports as not visible. That nothing shows was asserted through `IsWindowVisible`, not seen by eye, and the new test was not run against the old `Show(0, 0)`, since failing it puts a menu on the desktop.


## Viewer model

- [ ] A pair is still read and diffed before the viewer answers the test process that sent it (`MessageHandler.TrackMove`). The diff is bounded, so what is left is two reads: a million lines a side, shuffled, is about two of the sender's three seconds. Answering first and filling the entry in afterwards was looked at and left: `Refresh` does not open an entry at its first change, a second arrival of the pair while unread would be compared against a placeholder, the watch that would fill it has a budget and a hidden cadence, and every small pair would flash an empty pane.
- [ ] A queue of more than a hundred pending files is looked at a hundred a pass, so a row that is not on screen follows its file within `count / 100` passes. Looking every pass at the rows the queue column shows would be better, and was judged not worth the watch knowing the body's height and what is folded.
- [ ] A snapshot discarded, or settled by a test that started passing, while its own source file is being written by a bulk accept is written with the rest of the file. It is not counted. Closing it takes `InlineApplier.ApplyAll` asking, for each patch after the file is patched in memory and before its one write, whether it is still wanted, with the core answering from whether the claimed entry is still queued.
- [ ] A change to one entry still costs by the queue's length, a tenth of what it did: 0.1 to 0.24 ms at 2,000 entries, in asking `InlineQueue`, which copies its list and builds a key an item. The first snapshot of a solution, and a removal that leaves a solution with only files, still rebuild the whole list, and an attached viewer's `Sync` still projects the whole queue for each listing that changed.
- [ ] Queue labels are kept against the queue's list, so a list changed after it is put in a state would show stale labels. Nothing in the core does that.
- [ ] `ScreenPayload` cuts a row at half the window and a cell, on both native heads splitting the panes equally, which was read and not run. A head that stops doing that has to change `Screen.PaneCells`. A changed frame of CJK rows is still 1.7 ms at 4K, in classifying each cluster.
- [ ] Past its budget a diff goes by the lines that occur once on each side. Texts made mostly of repeated lines have few, and are still split wherever the search stopped.
- [ ] A bulk discard under way in an owning viewer is not on its listings, so an attached window or the tray sees the queue shrink with no progress and refuses nothing meanwhile. A discard-all from the tray during an accept-all now waits for it to end.
- [ ] `ScreenBuilder.Build` for an entry of 100,000 lines is 1.5 to 2.5 ms a screen. (noticed, cause not found)
- [ ] The rows a native head reports now depend on its footer, and the status line depends on the rows ("lines 1-N"). At a width where one character of status decides whether it gets a line of its own, the two could alternate frame to frame. Not seen; the WinForms head has always had the same loop.
- [ ] A drag in the pane that can go less far, from a centre beyond its own range, moves only the other pane's picture until the centre is back inside its range. All three heads.
- [ ] `DocumentWatch` does nothing for a window a head reports as unseen, as for a hidden one, so pages are not drawn until it is seen again. On macOS that now includes a wholly covered window: if `occlusionState` is ever wrong, pages stall.
- [ ] Both sides of a document are drawn at once, and four things about that could be better: a drawing is not stopped when the reader leaves its entry, though between two pages of a PDF it could be; the pages of a PDF that is put back because the other side stopped inside PDFium are dropped, and drawn again once PDFium is free; a PDF pair's right side waits for the left's first page, which is what lets the two be told apart when both stop; and `Withdrawn`, which takes a rendering back out of the state, lives in `DocumentWatch` where it belongs beside `ViewerSession.Rendered`.
- [ ] Which of two PDFs stopped inside PDFium is inferred from whose pages stopped first, not known. A thread descheduled between landing a page and asking for the lock, at the moment the other side hangs, would have the innocent side given up on and the culprit put back.


## Viewer, Windows head

- [ ] A status loses what is past three lines under the buttons, behind an ellipsis and a tooltip. The footer was not looked at on a scaled display; its tests enlarge the font instead.
- [ ] In a capture a footer taller than one row cuts the last rows of a screen built for the whole window, since a capture's screen is built before the footer is laid out. The window is not affected.
- [ ] Between half its own size and its own size an enlarged picture is still scaled on every paint: 9 ms for a 4000 by 3000 pair at 400%. A copy there would cost up to the decoded picture again, 96 MB for that pair.
- [ ] The picture baselines were not taken again for the premultiplied decode, apart from the two the footer moved. They differ from what is drawn by one level in translucent pixels and pass at the suite's 0.9999.
- [ ] The tests that raise, maximise or minimise a window run it on a desktop of the test process's own (`UnseenDesktop`). Where one cannot be made they run on the ordinary desktop, as before, and `AWindowShownThereNeverHasTheKeyboard` is skipped: not seen to happen, and CI's runners were not tried before this. Their windows are on an MTA thread, so a test that needs an apartment (the clipboard, drag and drop) cannot use it as it stands.
- [ ] `ImageCacheTests` and `FormsHeadTests.EveryPictureEverDrawnStaysDecoded` write to fixed folders under the temp folder (`deview-image-cache`, `deview-review-cache`), so two runs of the suite at once on one machine fail each other. (seen)
- [ ] In a window about 560 wide the left pane's header runs into the right pane's with no gap. It shows in `FooterThatWraps`. (noticed, not looked into)
- [ ] The test that an exception comes out of a message pump has only been seen to pass, since failing it is what shows the dialog.
- [ ] A window wholly behind another is not reported as unseen; only a minimised one is.


## Viewer, Linux head

- [ ] The rows reported are capped by what a tall footer leaves the body (ABI 12). The scene that holds it, `ATallFooterTakesRowsFromTheBody`, repeats a file pair's buttons at 1100 px, since the shared test window cannot be made 450 px wide: a real paged document in a narrow window was not run.
- [ ] The machine's fonts are drawn, not shaped: Arabic is unjoined and right to left text is in stored order. Colour emoji fonts and CFF2 variable fonts cannot be read by stb_truetype and are passed over, so a machine whose only CJK font is the variable Noto still shows replacement glyphs. At most fifteen fonts are merged.
- [ ] A window behind another is not known to be unseen, since GLFW passes nothing on from X11, so its watchers run as for one on screen. That a minimised window is left alone, and the drag, were checked by hand under Xvfb with openbox and xdotool, not in the suite: CI has neither.
- [ ] An idle window still turns sixty times a second: 3 to 9 ms of processor a second. Waiting on the window system instead would take the managed loop being told when to wake.
- [ ] Leaving a window alone was run only under Xvfb with Mesa's software rasteriser, with no window manager and under openbox. Not on a GPU, under a compositor, on Wayland or over forwarded X.
- [ ] Control held for the wheel to zoom is read from the key's physical state, so a latched Control (sticky keys) scrolls instead. GLFW's scroll callback carries no modifiers. (plausible)
- [ ] A picture brought down to the largest texture is drawn as that copy's pixels when enlarged past it, not its own, and nothing on screen says so. Bringing down a picture of 20,000 pixels square holds about 2 GB for a moment.
- [ ] A picture between half and three quarters of its size is drawn from its half size copy under about seven tenths: even, but soft. In a window made narrower, a picture is drawn smoothed for the frames until its reduced copies land, then changes once.
- [ ] Display scale is read once, as the window is made. A change of `Xft.dpi` while running, and a scale per monitor under Wayland or XWayland, are not followed, and a placement remembered at one scale is used in pixels at another. Checked only under Xvfb with an X resource at 144 and 192, never on a HiDPI desktop.
- [ ] `PixelTests.AWindowLeftAloneIsNotDrawn` and `AHiddenWindowIsNotDrawn` show a real window for a few seconds, which someone who turns the pixel tests on at a desktop will see, and the second asserts exactly one draw on the first present after showing.
- [ ] A press in a window that does not have input focus, with its release never arriving, was not tried. Hidden, unmapped, minimised and unfocused during a press, the drag stopped within a turn. A two finger tap on a real touchpad is unconfirmed.


## Viewer, macOS head

- [ ] `Runtime.open` cannot fail (it always returns true), so with no console session, as over SSH, AppKit aborts the process after the port was bound and the patch is lost. `CGSessionCopyCurrentDictionary()` may also answer "no session" over SSH while the same user is logged in at the console, where the viewer opens today. `SCDynamicStoreCopyConsoleUser` beside it would tell those apart except under fast user switching, which was not tried. What would settle it: print both from an SSH shell with and without a console login, and run the viewer in each. (reported: plausible)
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
- [ ] To confirm on a Mac, from the two rounds of smaller fixes: ten changes, eight of them event handling or window state that no capture exercises. The check for each is in its commit's message.
- [ ] To confirm on a Mac, from the ABI round. CI's job only captures, so a green build exercises none of the three:
  - A PDF pair in a window narrow enough for four rows of buttons has its last body row drawn above the footer, and "lines 1-N" in the status drops as the footer grows.
  - Miniaturise or wholly cover an owning viewer and rewrite a pending received file: the pane follows about a second later rather than within 200 ms, and at once after the window is uncovered.
  - With a wide and a tall image enlarged, drag the tall one to its bottom edge, then drag the wide one sideways: the tall one does not jump.
- [ ] A live resize still draws the rows sliced for the old size until the mouse comes up. It needs a frame callback in the C ABI. A press in the scroller's slot followed by a drag is still AppKit's loop too.
- [ ] A title's width is counted in cells, so a title of characters a fallback font draws wider than a cell can still reach the subtitle. One that fits exactly touches the subtitle with no gap, where Linux keeps a character.
- [ ] The zoom keys behind Option were not tried on any layout, and zoom reset (`0`) is matched on `charactersIgnoringModifiers` only.
- [ ] An enlarged pair that is the same picture in both panes is still drawn from the picture rather than from a copy, though with a copy kept for each pane it no longer has to be.
- [ ] Since macOS 11 a view with an automatic backing store is handed its whole bounds whatever was invalidated, clip included, so the clip test probably leaves nothing out on any supported macOS. A view of the spinner's own would make it pay only if AppKit gives that view a layer of its own, which cannot be checked from here. (read, in Apple's developer forums)
- [ ] A hidden, miniaturised or covered window comes forward up to a tenth of a second late for a patch arriving over the socket, since the managed side cannot interrupt the pump. That needs a way from the listener thread into AppKit's event loop.
