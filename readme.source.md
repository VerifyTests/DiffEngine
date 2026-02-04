# <img src="/src/icon.png" height="30px"> DiffEngine

[![Discussions](https://img.shields.io/badge/Verify-Discussions-yellow?svg=true&label=)](https://github.com/orgs/VerifyTests/discussions)
[![Build status](https://img.shields.io/appveyor/build/SimonCropp/DiffEngine)](https://ci.appveyor.com/project/SimonCropp/DiffEngine)
[![NuGet Status](https://img.shields.io/nuget/v/DiffEngine.svg?label=DiffEngine)](https://www.nuget.org/packages/DiffEngine/)
[![NuGet Status](https://img.shields.io/nuget/v/DiffEngineTray.svg?label=DiffEngineTray)](https://www.nuget.org/packages/DiffEngineTray/)

include: intro

**See [Milestones](../../milestones?state=closed) for release notes.**

**Currently used by:**

 * [ApprovalTests](https://github.com/approvals/ApprovalTests.Net)
 * [Shouldly](https://github.com/shouldly/shouldly/)
 * [Verify](https://github.com/VerifyTests/Verify)


## Sponsors

include: zzz


### JetBrains

[![JetBrains logo.](https://resources.jetbrains.com/storage/products/company/brand/logos/jetbrains.svg)](https://jb.gg/OpenSourceSupport)


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

There are also individual properties to check for each specific build system

snippet: BuildServerDetectorProps


## AiCliDetector

`AiCliDetector.Detected` returns true if the current code is running in an AI-powered CLI environment.

Supports:

 * [GitHub Copilot CLI](https://docs.github.com/en/copilot/using-github-copilot/using-github-copilot-in-the-command-line)
 * [Aider](https://aider.chat/docs/config/dotenv.html)
 * [Claude Code](https://docs.anthropic.com/en/docs/build-with-claude/claude-cli)

There are also individual properties to check for each specific AI CLI

snippet: AiCliDetectorProps


## Disable for a machine/process

Set an environment variable `DiffEngine_Disabled` with the value `true`.


## Disable in code

```
DiffRunner.Disabled = true;
```


## Icons

[Game](https://thenounproject.com/term/game/2956486/) designed by [Andrejs Kirma](https://thenounproject.com/andrejs/) from [The Noun Project](https://thenounproject.com).

Tray icons from [LineIcons](https://lineicons.com/icons/).