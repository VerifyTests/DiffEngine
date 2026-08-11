# DiffEngineTray

DiffEngineTray sits in the Windows tray. For supported snapshot testing libraries, it monitors pending changes in snapshots, and provides a mechanism for accepting those changes. It is intended as an alternative to using the clipboard as an approval mechanism.


## NuGet

 * https://www.nuget.org/packages/DiffEngineTray


## Installation

`dotnet tool install -g DiffEngineTray`


## Running

Run `diffenginetray` in a console to start the app.


## UI

<img src="..\src\DiffEngineTray.Tests\MenuBuilderTest.Full.verified.png">


### Grouping

Moves, deletes and pending snapshots are grouped by the containing solution. In the above, the files exist in DiffEngine, so they are grouped under it. The per-group headers act on that group only: clicking one solution's "Pending Snapshots" does not accept another's.


### Moves

"Pending Moves" will accept the changes to file3 and file4.

Clicking "file3" or "file4" will accept the changes to file3 or file4 respectively. The drop down will expose extra actions for that change.


### Deletes

A test can produce multiple resulting snapshots. If the accepted versions has a different number of snapshots to the current test run, then some of those snapshots need to be deleted. The delete functionality in the tray tool handles this scenario.

"Pending Deletes" will delete file1 and file2.

Clicking "file1" or "file2" will delete file1 or file2 respectively. The drop down will expose extra actions for that change.


### Pending snapshots

[Inline snapshots](/docs/viewer.md) are reviewed in DiffEngineViewer rather than in a diff tool.
The queue of them lives in whichever process claims the loopback port first, and stays there for as
long as that process runs. With no tray, that is the viewer. With one, it is normally the tray,
because the tray starts at login and the viewer only starts when a snapshot fails.

When the tray holds it, the window becomes disposable. A viewer that is closed, killed or crashes
takes nothing pending with it, and the tray opens a new one on the same queue. A snapshot arriving
with no window open starts one.

"Pending Snapshots" accepts all of them. Clicking one accepts that one, and its drop down offers
discard, opening the viewer on it, and opening the source file. A snapshot that failed to apply is
marked with `!` and stays pending, so it can be retried once whatever blocked it is out of the way.

A tray restart loses the queue, as it loses pending moves and deletes. Re-run the tests.


### Accept all

"Accept all" will accept all pending moves, deletes and inline snapshots. Snapshots whose target frameworks disagree about the content are skipped rather than picked between; resolve those in the viewer.


### Locked files

If accepting a move fails because the files are locked by another process (for example the snapshot is open in Microsoft Word), a prompt is shown listing the locked files and the locking processes:

<img src="..\src\DiffEngineTray.Tests\LockedFilesFormTests.Default.verified.png">

 * "Ignore" leaves the move pending so it can be accepted later.
 * "Kill [process] and accept" kills the locking processes and accepts the move.
 * "Kill and accept all pending" kills the locking processes and accepts all pending moves, killing any other locking processes without further prompts.
 * "Always kill" kills the locking processes and accepts the move. The choice is stored in settings, so future locked files are killed without prompting. It can be toggled in the Options dialog.


### Discard

Discard will clear all currently tracked items.


### Purge verified files

Prompts for a directory, and then recursively deletes all `*.verified.*` in that directory.


### Debug view

Everything currently tracked, as text: every field of every pending move, delete and snapshot, plus which process owns the inline queue.

<img src="..\src\DiffEngineTray.Tests\DebugFormTests.Default.verified.png">

The menu shows each pending item reduced to what fits on a line, a name and a few actions. This is the rest of it, for when the interesting part is a path, an argument list, or the process a diff tool was launched as. "Copy" puts the whole report on the clipboard, which is what to attach to an issue.

Nothing pushes at the window, so it shows the moment it was read. "Refresh" takes a newer reading.


### Options

<img src="..\src\DiffEngineTray.Tests\OptionsFormTests.Default.verified.png">


#### Run at startup

Runs DiffEngineTray at system startup.


#### Open on left

By default, when a diff is opened, the temp file will be on the left and the target file will be on the right. To invert this, select "Open on left".


#### Max instances to launch

Control the [max instances to launch setting](docs/diff-tool.md#maxinstancestolaunch).


#### Always kill locking processes

When accepting a move with [locked files](#locked-files), kill the locking processes without prompting.


#### Discard all HotKey

Registers a system wide HotKey to discard pending:

 * Deletes
 * Moves
 * Inline snapshots


#### Accept all HotKey

Registers a system wide HotKey to accept pending:

 * Deletes
 * Moves
 * Inline snapshots (conflicted ones are skipped)


#### Accept all open HotKey

Registers a system wide HotKey to accept pending:

 * Deletes
 * Moves that are currently open in a diff tool
 * Inline snapshots, all of which are open by definition: the viewer only stays running while it has something to show

To limit impact on system resources, the [default max concurrent open tool instances is limited to 5](/docs/diff-tool.md#maxinstancestolaunch).

Accept all open HotKey allows the current batch of open diffs to be accepted.


## Currently supported in

 * [ApprovalTests](https://github.com/approvals/ApprovalTests.Net) v5.4.0 and above
 * [Shouldly](https://github.com/shouldly/shouldly) v4.0.0 and above
 * [Verify](https://github.com/VerifyTests/Verify) v6.10.4 and above


## Payloads


### Add pending move

snippet: PiperTest.MoveJson.verified.txt


### Add pending delete

snippet: PiperTest.DeleteJson.verified.txt


## Logging Directory

Beside the installed tool, so it moves with the target framework the tray is built for:

```
%UserProfile%\.dotnet\tools\.store\diffenginetray\{VERSION}\diffenginetray\{VERSION}\tools\net10.0\any\logs
```

For example:

```
C:\Users\simon\.dotnet\tools\.store\diffenginetray\20.0.0\diffenginetray\20.0.0\tools\net10.0\any\logs
```

The menu's "Open logs" opens it without any of that.