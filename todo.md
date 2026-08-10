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

## 2. The verb dispatch is written twice (medium)

`MessageHandler` (viewer owns) and `OwnedInlineHost.Handle` (tray owns) are the same 12-verb
switch with the same validation and the same error strings — "Inline requires a body", the
`Remove` rejection, "No pending snapshot for {key}", "Queued {n}". The queue extraction removed
semantic drift; the *server behaviour* can still drift, which is the same class of bug.

Direction: extract the queue-facing verbs (`inline`, `settle`, `list`, `listfull`, `accept`,
`discard`, `acceptall`, `discardall`) into a shared handler in DiffEngine — it owns an
`InlineQueue`, a `Func<InlinePatch, InlineApplyResult>` and an `Action<WindowCommand, string?>`
sink. The viewer wraps it to project into `SessionState` (selection-follow on act); the tray wraps
it with the stash-and-launch behaviour. The window verbs (`focus`/`show`/`hide`/`quit`) stay
per-host, they are genuinely different.

Note if attempted: the viewer side routes through `ViewerSession` so the *display* updates with the
queue in one mutation. The shared handler would need the same seam `ViewerSession.Sync` already
provides.

## 3. File mode carries queue machinery for a queue of one (medium)

`ViewerSession.AcceptFile` / `AcceptAllFiles` / `DiscardFile` / `CopyOver` (~90 lines) generalize
over a queue that file mode can never grow: `RunFile` enqueues exactly one entry and runs with no
server. `AcceptAllFiles` is reachable only by pressing shift+A with a single item, where it equals
accept. Collapse to accept/discard-of-current and delete the loops. `SelectingResetsScroll`-class
tests do not cover file mode multi-entry because it cannot exist.

Same theme, smaller: `ViewerSession.Settle`/`Pending` assume inline entries (`_.Patch!`). A settle
against a file-mode session would NRE. Unreachable today (file mode has no socket), but a one-line
mode guard, like `Apply` already has, would make the invariant explicit instead of implicit.

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
