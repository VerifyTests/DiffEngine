# Review: 74d583d..HEAD (per-OS renderers + tray-owned queue)

53 commits, 237 files. The architecture landed coherently: one `Screen` model behind three
renderers, one `InlineQueue` behind two hosts, one protocol in one place, ownership decided once by
a port bind. The findings below are ordered by how much they matter, not by where they live.

## 1. ~~One slow accept can wedge the whole arrangement~~ (done)

All four legs landed:

- `InlineQueue` gained completion overloads (`Accept(entry, result)` and `AcceptAll(outcomes)`),
  so a host can find under its lock, apply outside it, and complete under it again — with the
  outcome semantics still written once. Completion no-ops when the entry was replaced or removed
  mid-apply.
- `OwnedInlineHost` applies outside the gate on every path, menu and socket, through those
  overloads.
- `Tracker.Accept` / `AcceptAllSnapshots` run on a worker and return the task for tests; the menu
  discards it and the balloon channel carries failures.
- `ViewerServer` handles each connection on its own task, and answers a throwing handler with an
  error instead of letting it vanish on an untracked task.
- `OwnerLink` waits 15s instead of 3s — refusal still fails in milliseconds, so gone is still
  detected fast; only busy stopped reading as dead.

Pinned by `AListingIsAnsweredWhileAnAcceptIsApplying` and
`AReplacedEntrySurvivesTheAcceptItInterrupted` (OwnedInlineHostTest),
`ASlowExchangeDoesNotBlockTheNext` and `AThrowingHandlerAnswersAnError` (ViewerProtocolTests, both
TFMs), `ABusyOwnerDoesNotReadAsDead` (AttachedViewerTests), and the completion tests in
InlineQueueTests.

Remaining, related but separate: in the *viewer-owns* arrangement, an accept clicked in the window
runs on the render thread inside `host.Mutate` — the window itself freezes for the mutex wait.
Socket-driven accepts don't stall rendering (lock-free `State` reads), only the user's own click
does. Same two-phase pattern would fix it; lower stakes because the user just asked for the work.

## 2. ~~The verb dispatch is written twice~~ (done)

`ViewerMessageHandler` in `Protocol/` now owns the twelve-verb switch, the validation and every
wire string, mapping onto `IQueueOwner` — the queue half of the protocol, implemented by
`MessageHandler` over the session (selection-follow stays in the same mutation) and by
`OwnedInlineHost` over its `InlineQueue` (stash-and-launch stays in its `Window`). Landed slightly
wider than sketched: the window verbs went through a `Window(command, key)` seam rather than
staying per-host, so the dispatch itself is not duplicated either.

One deliberate delta: a wire `focus` with an unknown key now errors on the tray as it always did on
the viewer, instead of stashing a focus for an entry that is not there. The tray's own menu path
(`IInlineHost.Focus`) is unaffected.

All 705 tests unchanged, including every wire-shape baseline.

## 3. ~~File mode carries queue machinery for a queue of one~~ (done)

`AcceptAllFiles` and `CopyOver` are gone; accept-all and discard-all in file mode now route to the
same accept and discard as their singular forms, which is what they always were with one entry.
`Settle` gained the mode guard. Net −63/+37 in `ViewerSession`.

Worth recording: this code had **no** test coverage at all — `Fixtures.File` was used only by
render and scroll tests, so file-mode accept and discard were never exercised. Five tests now
cover copy, a failed copy staying pending, accept-all equalling accept, both discard forms, and
the settle guard. Two message changes fell out, both improvements and both previously unasserted:
accept-all in file mode said "Accepted 1" and discard-all said "Discarded 1"; both now name the
comparison.

## 4. Small cleanups

- **`BundledViewerDirectory.Find(string?)` overload is dead.** Its doc comment says it exists for
  "a caller that knows where the bundle is" — but `TrayViewerDirectory` went the `AppContext`
  route instead, so the only caller of `Find(root)` is `Find()`. Collapse it back.
- **Stale renderer note in `Implementation/DiffEngineViewer.cs`.** `Notes` says "WinForms on
  Windows, Dear ImGui through raylib elsewhere" — macOS has been AppKit/Core Text since the Swift
  head. This text flows into the generated diff-tool docs, so regenerate after fixing.
- **`owned.Changed` is wired after the listener is already serving** (`Program.cs`). A patch
  arriving in that window lights the icon a scan late. Either pass `Changed` into `TryOwn` or
  accept the two-second worst case with a comment.
- **A stale accept from the tray menu succeeds silently.** `NotFound` drops the entry (right) and
  `OwnedInlineHost.Accept` reports success by count-decrease, so "source changed, re-run the test"
  goes to the log but never to the user. The viewer surfaces the same outcome in its footer. Cheap
  fix: balloon when the entry was dropped without `Applied`/`AlreadyApplied`.
- **`ViewerProgram.Run`'s optional `windowCommands` parameter** exists only so `RunAttached` can
  share the queue with its `OwnerLink`. Constructing the link inside `Run` (or passing a factory)
  would remove the nullable parameter and the `??= new()`.
- **`Tracker.Clear` still skips snapshots.** The plan noted this would become fixable once the
  tray owns the queue ("`Discard (3)` on one move and two snapshots discards one thing"), and the
  comment in `Clear` still describes the viewer-owned arrangement. With `IInlineHost` in hand it
  could discard-all when this tray owns; when remote, leaving the viewer's queue alone remains
  defensible.

## 5. Direction, not action: two servers in the tray

The tray now hosts two hand-rolled TCP protocols: Piper on 3492 (JSON-ish lines, fire-and-forget,
substring dispatch) for moves/deletes, and the viewer protocol on 3493 (versioned, base64,
request/response) for snapshots. Long-term the moves/deletes are two more verbs on the newer
protocol — one port, one codec, one listener, and `PiperClient`/`PiperServer` retire. Major-version
change because every Verify/ApprovalTests/Shouldly in the wild speaks Piper; not worth starting
until something else forces a protocol bump.

## 6. Process

- The plan's manual verification checklist has not been run: tray owning, viewer owning, tray
  started late, viewer killed while the tray owns, queue drained, tray restarted, images
  unchanged. The `[Explicit]` tests in `ViewerLaunchTests` are the harness.
- ~~The wedge cluster in item 1 has no automated coverage~~ — covered now; see item 1 for the
  test names.
