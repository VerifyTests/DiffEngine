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

Moves and deletes will be grouped by the containing solution. In the above, the files exist in DiffEngine, so they are grouped under it.


### Moves

"Pending Moves" will accept the changes to file3 and file4.

Clicking "file3" or "file4" will accept the changes to file3 or file4 respectively. The drop down will expose extra actions for that change.


### Deletes

A test can produce multiple resulting snapshots. If the accepted versions has a different number of snapshots to the current test run, then some of those snapshots need to be deleted. The delete functionality in the tray tool handles this scenario.

"Pending Deletes" will delete file1 and file2.

Clicking "file1" or "file2" will delete file1 or file2 respectively. The drop down will expose extra actions for that change.


### Accept all

"Accept all" will accept all pending moves and all pending deletes.


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


#### Accept all HotKey

Registers a system wide HotKey to accept pending:

 * Deletes
 * Moves


#### Accept all open HotKey

Registers a system wide HotKey to accept pending:

 * Deletes
 * Moves that are currently open in a diff tool

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

```
%UserProfile%\.dotnet\tools\.store\diffenginetray\{VERSION}\diffenginetray\{VERSION}\tools\net8.0\any\logs
```

For example:

```
C:\Users\simon\.dotnet\tools\.store\diffenginetray\9.0.0\diffenginetray\9.0.0\tools\net8.0\any\logs
```