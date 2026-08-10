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

## 4. ~~Small cleanups~~ (done)

All six. The two that turned out to be more than tidying:

- **A stale accept reported as success.** `NotFound` drops the entry, and reporting by
  count-decrease made that indistinguishable from applied — the snapshot vanished from the menu
  and the user was never told to re-run. `IInlineHost.Accept` now returns an `AcceptOutcome`
  (`Unknown`/`Applied`/`Stale`/`Failed`) taken from the apply status rather than inferred from the
  queue, and the tray balloons on `Stale`. `RemoteInlineHost` can only say Applied or Failed,
  because the wire carries `ok` and a message rather than an apply status — noted in place, and it
  costs nothing there since a viewer owner is displaying that message itself.
- **`Tracker.Clear` skipping snapshots** was worse than recorded: it cleared only the local cache,
  so "Discard (3)" discarded one thing *and* the other two reappeared on the next scan. Now
  discards through `IInlineHost.DiscardAll` in both arrangements, since the count in the label
  includes them either way.

The other four: the dead `Find(string?)` overload collapsed back; the tool `Definition` Notes now
name all three renderers (regenerated into `docs/diff-tool.md`); `OwnedInlineHost.Start()` split
from `TryOwn` so binding claims ownership early while serving waits until `Changed` is wired; and
`OwnerLink` owns the `ConcurrentQueue<WindowCommand>` it produces into, which removed
`ViewerProgram.Run`'s optional parameter and a constructor argument.

Found while doing it, and fixed: the item-1 completion path reported `Applied` when a re-run had
replaced the entry mid-apply. The patch did reach the file, but the queue had moved on to a newer
one, so the outcome described nothing the caller still had. Now `Unknown`, which is what the
pre-existing test `AReplacedEntrySurvivesTheAcceptItInterrupted` was asserting through the old
bool shape — it caught the regression.

## 5. Resolved: the two servers in the tray stay two

Earlier revisions of this note called the second listener debt and sketched "one port, one codec,
one listener". Wrong, on inspection — the ports answer different questions and cannot merge in
either direction:

- **3492 means "a tray is here."** Moves and deletes need the tracker, the balloon, the menu;
  only the tray can serve them.
- **3493 means "the inline queue owner is here"** — tray *or* viewer, decided by bind.
- Collapse them and the late-tray case breaks: today a viewer can own the queue on 3493 while the
  tray still receives every move on 3492. One port cannot serve that, and a standalone viewer
  would have to squat the tray's port to own its queue.

Nor should the queue protocol fold into Piper's format: Piper is the frozen side. Every stable
DiffEngine since ~15.x embeds `PiperClient`, pinned inside test projects while the tray updates
independently, so old-library + new-tray is the normal pairing. Piper is also one-way — the server
never writes, the client never reads, unknown payloads are deliberately ignored — and the queue
protocol is request/response at its core.

What could ever unify, fully back-compat, is the codec on 3492: sniff `version:` vs `"Type"` and
answer the new format while accepting the old. But fire-and-forget means a new library can never
detect an old tray ignoring the new format, so legacy emission stays forever, both readers stay
forever, and the net is plus one format, minus nothing. The one real prize available additively is
an ack for moves — today the library cannot know the tray received one. Do that as new verbs on
3492's listener if and when it is actually wanted; nothing requires a major version.

## 6. Process

- The plan's manual verification checklist has not been run: tray owning, viewer owning, tray
  started late, viewer killed while the tray owns, queue drained, tray restarted, images
  unchanged. The `[Explicit]` tests in `ViewerLaunchTests` are the harness.
- ~~The wedge cluster in item 1 has no automated coverage~~ — covered now; see item 1 for the
  test names.
