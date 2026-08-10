# Inline snapshots

The plumbing DiffEngine provides for reviewing
[inline snapshots](https://github.com/VerifyTests/Verify/blob/main/docs/inline-snapshots.md):
carrying a pending edit from the test process to a reviewer, showing it, and splicing it into
the source file once accepted. Verify is the producing side; this page documents the DiffEngine
half, for anyone integrating with it — a test library, an IDE plugin, or another review surface.


## The parts

Five parties, three transports: two loopback ports, stdin for a cold launch, and staged files
for when no viewer is available.

```mermaid
flowchart LR
    subgraph Test["test process"]
        Verify["Verify"] --> Engine["DiffEngine library"]
    end
    Tray["DiffEngineTray"]
    Window["DiffEngineViewer window"]
    Plugin["ReSharper / Rider plugin"]
    Owner{{"inline queue owner: whoever bound 3493<br/>first — the tray at login, else a viewer"}}
    Files[("source files and<br/>staged patch files")]

    Engine -->|"3492 moves, deletes (one way)"| Tray
    Engine -->|"3493 inline, settle"| Owner
    Engine -.->|"launch with patch on stdin,<br/>when nothing owns 3493"| Window
    Tray <-->|"3493 list, accept, focus"| Owner
    Window <-->|"3493 listfull, accept, discard"| Owner
    Plugin -->|"3493 settle, after accepting"| Owner
    Owner -->|"InlineApplier"| Files
    Plugin -->|"InlineApplier"| Files
```

The queue of pending snapshots has exactly one owner per session: whichever process bound port
3493 first, decided once and never transferred. When the tray owns it, its edges to the owner
above are in-process calls; when a viewer owns it, the tray drives that viewer over the same
verbs. Either way both hosts run the same `InlineQueue` implementation, so they cannot disagree
on what accepting or settling means. [DiffEngineViewer](/docs/viewer.md) and
[DiffEngineTray](/docs/tray.md) cover the two arrangements in detail.


## When a test fails

```mermaid
sequenceDiagram
    participant Test as test process
    participant Owner as queue owner (3493)
    participant Window as viewer window

    Test->>Test: literal differs: build InlinePatch
    alt something owns 3493 (usually the tray)
        Test->>Owner: inline, over the socket
        Owner->>Window: focus, launching --attach if no window
    else nothing bound, bundled viewer resolves
        Test->>Window: launch, patch on stdin
        Window->>Window: bind 3493, own the queue
    else no viewer, or DiffEngine_InlineViewer=false
        Test->>Test: stage received / expected / patch files
    end
    Window->>Owner: accept
    Owner->>Owner: InlineApplier rewrites the literal
    Test->>Owner: re-run passes: settle
    Owner->>Owner: drop the entry
```

Nothing touches disk on the happy path: a running owner receives the patch over the socket, a
newly launched viewer receives it on stdin. Staging only happens in the fallback, where the test
library writes three files (Verify: `*.received.txt`, `*.expected.txt`, `*.inlinepatch`) so the
snapshot can still be reviewed by an IDE plugin, a plain text diff tool, or by hand:

```
DiffEngineViewer --inline --source <file.cs> --line <number> < the.inlinepatch
```


## Library API

For the producing side — a test library with a failing inline snapshot:

* `DiffRunner.AddInlineAsync(patch)` queues a patch with whatever owns the port, launching the
  bundled viewer when nothing does. Returns `Queued`, `Disabled` (build servers, continuous
  testing and AI CLIs included), or `NoViewerFound` — the caller's cue to stage files and fall
  back to a text diff.
* `DiffRunner.SettleInline(sourceFile, line)` drops the pending entry for a call site, for when
  a previously failing test passes. Unknown entries and an absent owner are no-ops, so call it
  freely.
* Setting `DiffEngine_InlineViewer` to `false` reports `NoViewerFound` without probing, which is
  how a user opts into reviewing in their IDE instead of a window.


## The patch file

`InlinePatchFile` reads and writes the wire and staging format for a patch. Plain text, content
fields base64 encoded so snapshot text needs no escaping and no JSON dependency:

```
version: 2
sourceFile: C:\code\project\tests\SampleTests.cs
lineHint: 42
mode: Set
originalExpression: {base64}
newContent: {base64}
```

`lineHint` is a hint: locating the call is content anchored, so a file that shifted since the
test run still patches, and one whose call site changed reports rather than corrupts. `mode` is
`Set` (replace or insert the expected argument), `Append` (add a Snapshot call where none exists
yet), or `Remove` (delete the call, used when migrating a snapshot back to a file).


## Applying a patch from another surface

The contract for a review surface of its own, which is what the ReSharper / Rider plugin is:
read the staged patch with `InlinePatchFile.TryRead`, apply it with `InlineApplier.Apply`, and
honour two rules.

* **InlineApplier owns all locking.** A per file cross process mutex (up to a ten second wait)
  plus an in process gate serialise every writer, so applying beside a concurrently accepting
  tray or viewer is safe, and callers must not add locking of their own. The file's encoding,
  BOM and line endings are preserved.
* **Settle what was applied.** The same test run that staged the files may also have queued the
  patch with the port owner, and that queue outlives both the window and the run. After
  `Applied` or `AlreadyApplied`, call `DiffRunner.SettleInline(patch.SourceFile,
  patch.LineHint)` — otherwise the tray keeps offering a snapshot that is already in the source.

`Apply` returns `Applied`, `AlreadyApplied` (the literal already matches), `NotFound` (the
source changed since the test run — tell the user to re-run rather than retrying), or a failure
with a message (locked file, unreadable source), which is retryable.

`Remove` mode patches are configuration changes with nothing to review: apply them directly;
`AddInlineAsync` refuses them.


## Ports

| Port | Meaning | Protocol |
| --- | --- | --- |
| 3492 | a tray is here | one way payloads: moves and deletes ([tray](/docs/tray.md#payloads)) |
| 3493 | the inline queue owner is here | request/response verbs, internal |

Two ports because they answer different questions: the owner of 3493 is sometimes a viewer, and
a late starting tray still receives every move on 3492 while it is. `DiffEngine_ViewerPort`
overrides 3493, which test suites use to keep out of the way of a live tray. The 3493 protocol
is internal and versioned; integrate through `DiffRunner` and `InlineApplier` rather than
speaking it directly.
