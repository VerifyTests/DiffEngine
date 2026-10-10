# Diff Tools


## Initial difference behavior

Behavior when an input is verified for the first time.

Behavior depends on if an [EmptyFiles](https://github.com/SimonCropp/EmptyFiles) can be found matching the current extension.

 * If an EmptyFiles can be found matching the current extension, then the tool will be launched to compare the input to that empty file.
 * If no EmptyFiles can be found no tool will be launched.

The bundled [DiffEngineViewer](/docs/viewer.md) is the exception. It needs no file to compare against, so none is written: the input is shown against an empty side, and stays that way until it is accepted. That holds for every extension, including those with no EmptyFiles.


## Detected difference behavior

Behavior when a difference is detected between the input and an existing current verified file.


### Not Running

If no tool is running for the comparison of the current verification (per test), a new tool instance will be launched.


### Is Running

If a tool is running for the comparison of the current verification (per test), and a new verification fails, the following logic will be applied:

| Auto Refresh | Mdi   | Behavior |
|--------------|-------|----------|
| true         | true  | No action. Current instance will refresh |
| true         | false | No action. Current instance will refresh |
| false        | true  | Open new instance. Previous instance must be manually closed |
| false        | false | Kill current and open new instance |

The bundled [DiffEngineViewer](/docs/viewer.md) is the exception: it queues every failing pair into one window, so none of the four rows describes it.

include: diffToolCleanup


## MaxInstancesToLaunch

By default a maximum of 5 tool instances will be launched. This prevents a change that breaks many tests from causing too much load on a machine.

This value can be changed using an environment variable or by explicitly specifying the value by code. When both are used, the value set in code wins; the environment variable is the ambient default for a run that sets nothing.

The count includes [DiffEngineViewer](/docs/viewer.md), but only when a viewer has to be started. Handing a pair to one already on screen opens no window and so spends nothing.

Neither does a file [derived from a document](/docs/viewer.md#files-derived-from-a-document) the viewer is drawing: it is shown beneath the document rather than in a tool of its own. A document split into a file per page would otherwise spend the whole allowance on one test.


### Using an environment variable

Setting the `DiffEngine_MaxInstances` environment variable to the number of instances to launch.

This value can also be set using [the DiffEngineTray options dialog](/docs/tray.md#max-instances-to-launch).


### Using code

snippet: MaxInstancesToLaunch


## Left/Right diff behavior

By default, when a diff is opened, the temp file is on the left and the target file is on the right.

This value can be changed by setting the `DiffEngine_TargetOnLeft` environment variable to `true`.

This value can also be set using [the DiffEngineTray options dialog](/docs/tray.md#open-on-left).


## Successful verification behavior

If a tool is running for the comparison of the current verification (per test), and a new verification passes, the following logic will be applied:

| Mdi   | Behavior |
|-------|----------|
| true  | No action taken. Previous instance must be manually closed |
| false | Kill current instance |

include: diffToolCleanup


## Disable orphaned process detection

Resharper has a feature [Check for orphaned processes spawned by test runner](https://www.jetbrains.com/help/resharper/Reference__Options__Tools__Unit_Testing__Test_Runner.html).

> By default, ReSharper maintains a list of all processes that are launched by the executed tests. If some of theses processes do not exit after the test execution is over, ReSharper will suggest you to terminate the process. If your setup requires some processes started by the tests to continue running, you can clear this checkbox to avoid unnecessary notifications.

Since this project launches diff tools, it will trigger this feature and a dialog will show:

> All unit tests are finished, but child processes spawned by the test runner process are still running. Terminate child process?

<img src="resharper-spawned.png" alt="R# terminate process dialog" width="400">

As such this feature needs to be disabled:


### Disable for solution

Add the following to `[Solution].sln.DotSettings`.

```
<s:String x:Key="/Default/Housekeeping/UnitTestingMru/UnitTestRunner/SpawnedProcessesResponse/@EntryValue">DoNothing</s:String>
```


### Disable for machine


#### Resharper

ReSharper | Options | Tools | Unit Testing | Test Runner

<img src="resharper-ignore-spawned.png" alt="Disable R# orphaned processes detection" width="400">


#### Rider

File | Settings | Manage Layers | This computer | Edit Layer | Build, Execution, Deployment | Unit Testing | Test Runner

<img src="rider-ignore-spawned.png" alt="Disable R# orphaned processes detection" width="500">


## Windows Subsystem for Linux

A test run inside a [WSL](https://learn.microsoft.com/en-us/windows/wsl/about) distribution can use a diff tool installed on Windows, as well as one installed in the distribution.

For each tool the distribution is searched first. A tool not found there is looked for on Windows: in the directories listed for it under [Supported Tools](#supported-tools), then on the `PATH`, which WSL extends with the Windows one. It is started through WSL's [interoperability with Windows](https://learn.microsoft.com/en-us/windows/wsl/filesystems#run-windows-tools-from-linux), and handed both files as Windows paths:

 * A file on a mounted drive by its drive letter: `/mnt/c/code/Tests.Method.received.txt` as `C:\code\Tests.Method.received.txt`.
 * A file inside the distribution as a share: `/home/user/code/Tests.Method.received.txt` as `\\wsl.localhost\Ubuntu\home\user\code\Tests.Method.received.txt`.

The [tool order](/docs/diff-tool.order.md) applies as it does anywhere else, so a Windows tool earlier in the order is used ahead of a tool in the distribution that is later in it.

`DiffEngine_{ToolName}` can point at the Windows copy of a tool, written either as the distribution sees the directory (`/mnt/c/Program Files/WinMerge`) or as Windows does (`C:\Program Files\WinMerge`).

What differs from a test run on Windows:

 * A Windows tool is only found again while the terminal session that started it is open. WSL runs a Windows program through a process of its own, which is how the tool is recognized, and that process ends with the session while the tool's window stays. A tool left open past its session is not closed when its test passes, and gets a second window when the same comparison fails again.
 * Visual Studio Code is started by its `code` launcher, which WSL places on the `PATH`, and opens both files through its WSL extension. A tool that is a script on Windows (`.cmd`) cannot be started from a distribution.
 * Vim and Neovim are only used from the distribution, since they run in the terminal they are started from.
 * [DiffEngineTray](/docs/tray.md) is not supported from inside a distribution.

To use only the tools in the distribution, set an environment variable `DiffEngine_WslWindowsTools` with the value `false`.

A distribution is treated as a developer machine and not as a build server: see [BuildServerDetector](/readme.md#wsl).


## Supported Tools:

Tools location is automatically detected. If a tool installed in a custom location, it can be manually configured using an environment variable, that points to the executable. The environment variable format is `DiffEngine_{ToolName}`.

include: diffTools