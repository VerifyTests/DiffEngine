# Diff Tools


## Initial difference behavior

Behavior when an input is verified for the first time.

Behavior depends on if an [EmptyFiles](https://github.com/SimonCropp/EmptyFiles) can be found matching the current extension.

 * If an EmptyFiles can be found matching the current extension, then the tool will be launch to compare the input to that empty file.
 * If no EmptyFiles can be found no tool will be launched.


## Detected difference behavior

Behavior when a difference is detected between the input an existing current verified file.


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

include: diffToolCleanup


## MaxInstancesToLaunch

By default a maximum of 5 tool instances will be launched. This prevents a change that break many test from causing too much load on a machine.

This value can be changed:


### Using an environment variable

Setting the `DiffEngine_MaxInstances` environment variable to the number of instances to launch.


### Using code

snippet: MaxInstancesToLaunch


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

Since this project launches diff tools, it will trigger this feature an a dialog will show:

> All unit tests are finished, but chid processes spawned by the test runner proces are still running. Terminate child process?

<img src="TerminateProcess.png" alt="R# terminate process dialog" width="400">

As such this feature needs to be disabled:

ReSharper | Options | Tools | Unit Testing | Test Runner

<img src="DisableProcessDetection.png" alt="Disable R# orphaned processes detection" width="400">


## Supported Tools:

include: diffTools
