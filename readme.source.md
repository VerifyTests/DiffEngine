# <img src="/src/icon.png" height="30px"> DiffEngine

[![Discussions](https://img.shields.io/badge/Verify-Discussions-yellow?svg=true&label=)](https://github.com/orgs/VerifyTests/discussions) [![Build status](https://github.com/VerifyTests/DiffEngine/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/VerifyTests/DiffEngine/actions/workflows/build.yml) [![NuGet Status](https://img.shields.io/nuget/v/DiffEngine.svg?label=DiffEngine)](https://www.nuget.org/packages/DiffEngine/) [![NuGet Status](https://img.shields.io/nuget/v/DiffEngineTray.svg?label=DiffEngineTray)](https://www.nuget.org/packages/DiffEngineTray/) [![NuGet Status](https://img.shields.io/nuget/v/DiffEngineViewer.Windows.svg?label=DiffEngineViewer.Windows)](https://www.nuget.org/packages/DiffEngineViewer.Windows/) [![NuGet Status](https://img.shields.io/nuget/v/DiffEngineViewer.Mac.svg?label=DiffEngineViewer.Mac)](https://www.nuget.org/packages/DiffEngineViewer.Mac/) [![NuGet Status](https://img.shields.io/nuget/v/DiffEngineViewer.Linux.svg?label=DiffEngineViewer.Linux)](https://www.nuget.org/packages/DiffEngineViewer.Linux/)

include: intro

**See [Milestones](../../milestones?state=closed) for release notes.**

**Currently used by:**

 * [ApprovalTests](https://github.com/approvals/ApprovalTests.Net)
 * [Shouldly](https://github.com/shouldly/shouldly/)
 * [Verify](https://github.com/VerifyTests/Verify)


## Sponsors

include: sponsors



toc
include: doc-index


## NuGet

 * https://nuget.org/packages/DiffEngine/


## [Supported Tools](/docs/diff-tool.md#supported-tools)

include: diffToolList


## Launching a tool

A tool can be launched using the following:

snippet: DiffRunnerLaunch

Note that this method will respect the above [difference behavior](/docs/diff-tool.md#detected-difference-behavior) in terms of Auto refresh and MDI behaviors.


### Files derived from another

A snapshot of a document is often several files: the document, and what was computed from it, such as a png of each page, its text, or a csv per sheet. A file of the second kind can be launched as derived from the first:

snippet: DiffRunnerLaunchDerived

The source is named by its temp file, is launched first, and is named only while it is itself pending.

This changes nothing for most tools: each derived file is launched exactly as `Launch` would launch it. The exception is [DiffEngineViewer](/docs/viewer.md#files-derived-from-a-document) when it is drawing the source as a document. It already shows the pages and the text, so the derived files open no tool, do not count towards [MaxInstancesToLaunch](/docs/diff-tool.md#maxinstancestolaunch), and are accepted or discarded together with the document.


## Closing a tool

A tool can be closed using the following:

snippet: DiffRunnerKill

Note that this method will respect the above [difference behavior](/docs/diff-tool.md#detected-difference-behavior) in terms of MDI behavior.


## File type detection

DiffEngine use [EmptyFiles](https://github.com/SimonCropp/EmptyFiles) to determine if a given file or extension is a binary or text. Custom extensions can be added, or existing ones changed.


## BuildServerDetector

`BuildServerDetector.Detected` returns true if the current code is running on a build/CI server.

Supports:

 * [AppVeyor](https://www.appveyor.com/docs/environment-variables/)
 * [Travis](https://docs.travis-ci.com/user/environment-variables/#default-environment-variables)
 * [Jenkins](https://wiki.jenkins.io/display/JENKINS/Building+a+software+project#Buildingasoftwareproject-belowJenkinsSetEnvironmentVariables)
 * [GitHub Actions](https://help.github.com/en/actions/automating-your-workflow-with-github-actions/using-environment-variables#default-environment-variables)
 * [AzureDevops](https://docs.microsoft.com/en-us/azure/devops/pipelines/build/variables?view=azure-devops&tabs=yaml#agent-variables)
 * [TeamCity](https://www.jetbrains.com/help/teamcity/predefined-build-parameters.html#PredefinedBuildParameters-ServerBuildProperties)
 * [MyGet](https://docs.myget.org/docs/reference/build-services#Available_Environment_Variables)
 * [GitLab](https://docs.gitlab.com/ee/ci/variables/predefined_variables.html)
 * [GoCD](https://docs.gocd.org/current/faq/dev_use_current_revision_in_build.html)
 * [Bitbucket Pipelines](https://support.atlassian.com/bitbucket-cloud/docs/variables-and-secrets/#Default-variables)

There are also individual properties to check for each specific build system

snippet: BuildServerDetectorProps


### WSL

Running under [WSL](https://learn.microsoft.com/en-us/windows/wsl/about) is not treated as a build server, since a WSL session is usually a developer machine. `BuildServerDetector.IsWsl` reports it, and `BuildServerDetector.Detected` ignores it. Diff tools are launched there, the ones installed on Windows included: see [Windows Subsystem for Linux](/docs/diff-tool.md#windows-subsystem-for-linux).

A build that runs its tests inside WSL from a Windows build agent is the case to watch. The variables the build server sets exist on the Windows side, and WSL only passes across the ones named in [WSLENV](https://learn.microsoft.com/en-us/windows/wsl/filesystems#share-environment-variables-between-windows-and-wsl-with-wslenv). So inside WSL nothing is detected, and diff tools are launched on the build agent.

To have such a build detected, either name the build server's variable in `WSLENV` on the Windows side, for example on Azure DevOps:

```
set WSLENV=TF_BUILD:%WSLENV%
```

or set one of the detected variables on the command that starts the tests:

```
wsl -- env TF_BUILD=True dotnet test
```

To treat every WSL run as a build server, in a test:

```cs
BuildServerDetector.Detected = BuildServerDetector.Detected || BuildServerDetector.IsWsl;
```

That value is scoped to the current async context, as described below.


### Override in tests

`BuildServerDetector.Detected` can be set at test time. The value is stored in an `AsyncLocal`, so it is scoped to the current async context and does not leak to other threads or tests running in parallel.

snippet: BuildServerDetectorDetectedOverride


## Automatic AI detection

DiffEngine automatically detects when it is running inside an AI-powered CLI environment and disables the diff tool launch. This means no `DiffEngine_Disabled=true` environment variable is required when running tests from within an AI CLI — it is detected and handled automatically.

Supported AI CLIs:

 * [GitHub Copilot CLI](https://docs.github.com/en/copilot/using-github-copilot/using-github-copilot-in-the-command-line)
 * [Aider](https://aider.chat/docs/config/dotenv.html)
 * [Claude Code](https://docs.anthropic.com/en/docs/build-with-claude/claude-cli)


### Programmatic usage

`AiCliDetector.Detected` returns true if the current code is running in an AI-powered CLI environment.

There are also individual properties to check for each specific AI CLI

snippet: AiCliDetectorProps


## Disable for a machine/process

Set an environment variable `DiffEngine_Disabled` with the value `true`.


## Disable in code

```
DiffRunner.Disabled = true;
```


## Disable the tray

Pending moves and deletes are sent to [DiffEngineTray](/docs/tray.md) when one is running. That tracking is separate from launching a diff tool, so disabling diff does not stop it.

Set an environment variable `DiffEngine_TrayDisabled` with the value `true`, or in code:

```
DiffRunner.TrayDisabled = true;
```

[More detail](/docs/tray.md#opting-out-of-tracking).


## Icons

[Game](https://thenounproject.com/term/game/2956486/) designed by [Andrejs Kirma](https://thenounproject.com/andrejs/) from [The Noun Project](https://thenounproject.com).

Tray icons from [LineIcons](https://lineicons.com/icons/).