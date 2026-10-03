# Review todo

Findings from a review of `main` at 991bc480 (2026-10-03). The list from the review at 4244ebe6 is closed, so this one weights what has landed since: documents and maps, the text diff, zoom and pan, the remembered window and views, and pictures decoded off the UI thread.

- **reproduced**: run and seen. Either the API involved, or the repo's own sources compiled into a scratch console project outside the repo (the same files `DiffEngineViewer.csproj` links, plus a `Program.cs`). No test in the repo yet.
- **measured**: timed in that same project. Release, net10.0, this machine.
- **read**: confirmed by reading the code path end to end, not run.
- **cannot verify here**: needs macOS or Linux; the item says what would settle it.


## Bugs

Nothing open. The five this review found are fixed, each with tests that fail without the fix: a new snapshot reaching the viewer with no placeholder written, a re-sent pair leaving the reader where they are, document reading surviving a copy that could not be written, PDFs read again once a slow one returns, and a setting put back to its default staying there.


## Performance

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
  - `src/DiffEngineViewer/Documents/DocumentWatch.cs:218-226`: `Draw` renders the first side without pages to completion and returns. The right side starts on the next pass.
  - 100 A4 pages take 4.3 s here and the first lands after 87 ms. So the left page is on screen at once, and the right pane spins for 4.3 s, and which pages differ is unknown until both are done.
  - Fix: draw both sides at once on two tasks. Two PDFs then take turns at PDFium's lock a page at a time, and two Office files use two cores. The timeout, its `progressed` clock and `generation` become per job.

- [ ] **Every pending file is statted five times a second, and the owning watch never slows down** (measured)
  - `src/DiffEngineViewer/TrackedWatch.cs:27-55` and `src/DiffEngineViewer/Ipc/OwnerLink.cs:344-432`. One pass over 1,000 pending moves is 2,000 stats and takes 25 ms here, so 125 ms of every second. The class doc's "a queue is small enough that the difference is not measurable" holds to about a hundred.
  - `OwnerLink` drops to one pass a second when the window is hidden and `DocumentWatch` stops. `TrackedWatch` has no `Hidden`, and an owning viewer hidden behind a tray keeps its 200 ms for days.
  - Fix: give `TrackedWatch` the hidden interval. If large queues matter, stat the entry on screen every pass and the rest in turn.


## Smaller

- [ ] A pair sent again unchanged is still read and diffed before it is found to be unchanged: `MessageHandler.TrackMove` builds the whole entry, and `ViewerSession.EnqueueTracked` compares after. The reader is no longer moved, but a large pair pays the diff on every run. Fix: read the two sides, compare them with the queued entry's, and build an entry only when they differ. (read)
- [ ] `QueueProjection.Order` runs twice per transition: `Project` orders (`ViewerSession.cs:1530`) and `Rebuild` (`:1490`) and `Sync` (`:279`) order its result again. (read)
- [ ] Opening a context menu builds each side's whole text to ask whether it is empty: `SelectionText.All(entry, side).Length > 0` in `src/DiffEngineViewer/MenuState.cs:98` and `:117`. Megabytes per right-click on a large file. (read)
- [ ] `FileSide.ReadBytes` copies every file twice, through a growing `MemoryStream` and then `ToArray` (`src/DiffEngineViewer/FileSide.cs:105`). The length is known. (read)
