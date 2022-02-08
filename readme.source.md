# <img src="/src/icon.png" height="30px"> DiffEngine

[![Build status](https://ci.appveyor.com/api/projects/status/b62ti1b998iy3njw/branch/main?svg=true)](https://ci.appveyor.com/project/SimonCropp/DiffEngine)
[![NuGet Status](https://img.shields.io/nuget/v/DiffEngine.svg?label=DiffEngine)](https://www.nuget.org/packages/DiffEngine/)
[![NuGet Status](https://img.shields.io/nuget/v/DiffEngineTray.svg?label=DiffEngineTray)](https://www.nuget.org/packages/DiffEngineTray/)


DiffEngine manages launching and cleanup of diff tools. It is designed to be used by any Snapshot/Approval testing library.

**Currently used by:**

 * [ApprovalTests](https://github.com/approvals/ApprovalTests.Net)
 * [Shouldly](https://github.com/shouldly/shouldly/)
 * [Verify](https://github.com/VerifyTests/Verify)


toc
include: doc-index


## NuGet package

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
 * [Bamboo](https://confluence.atlassian.com/bamboo/bamboo-variables-289277087.html)


## Disable for a machine/process

Set an environment variable `DiffEngine_Disabled` with the value `true`.


## Icons

[Game](https://thenounproject.com/term/game/2956486/) designed by [Andrejs Kirma](https://thenounproject.com/andrejs/) from [The Noun Project](https://thenounproject.com).

Tray icons from [LineIcons](https://lineicons.com/icons/).