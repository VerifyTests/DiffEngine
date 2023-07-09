<!--
GENERATED FILE - DO NOT EDIT
This file was generated by [MarkdownSnippets](https://github.com/SimonCropp/MarkdownSnippets).
Source File: /readme.source.md
To change this file edit the source file and then run MarkdownSnippets.
-->

# <img src="/src/icon.png" height="30px"> DiffEngine

[![Discussions](https://img.shields.io/badge/Verify-Discussions-yellow?svg=true&label=)](https://github.com/orgs/VerifyTests/discussions)
[![Build status](https://ci.appveyor.com/api/projects/status/b62ti1b998iy3njw/branch/main?svg=true)](https://ci.appveyor.com/project/SimonCropp/DiffEngine)
[![NuGet Status](https://img.shields.io/nuget/v/DiffEngine.svg?label=DiffEngine)](https://www.nuget.org/packages/DiffEngine/)
[![NuGet Status](https://img.shields.io/nuget/v/DiffEngineTray.svg?label=DiffEngineTray)](https://www.nuget.org/packages/DiffEngineTray/)


DiffEngine manages launching and cleanup of diff tools. It is designed to be used by any Snapshot/Approval testing library.

**Currently used by:**

 * [ApprovalTests](https://github.com/approvals/ApprovalTests.Net)
 * [Shouldly](https://github.com/shouldly/shouldly/)
 * [Verify](https://github.com/VerifyTests/Verify)


<!-- toc -->
## Contents

  * [NuGet package](#nuget-package)
  * [Supported Tools](#supported-tools)
  * [Launching a tool](#launching-a-tool)
  * [Closing a tool](#closing-a-tool)
  * [File type detection](#file-type-detection)
  * [BuildServerDetector](#buildserverdetector)
  * [Disable for a machine/process](#disable-for-a-machineprocess)
  * [Disable in code](#disable-in-code)
  * [Icons](#icons)<!-- endToc -->
  * [Tools](/docs/diff-tool.md) <!-- include: doc-index. path: /docs/mdsource/doc-index.include.md -->
  * [Tool Order](/docs/diff-tool.order.md)
  * [Custom Tool](/docs/diff-tool.custom.md)
  * [DiffEngineTray](/docs/tray.md)
  * [Code versus machine level settings](/docs/code-versus-machine-settings.md) <!-- endInclude -->


## NuGet package

 * https://nuget.org/packages/DiffEngine/


## [Supported Tools](/docs/diff-tool.md#supported-tools)

 * **[BeyondCompare](/docs/diff-tool.md#beyondcompare)** Win/OSX/Linux (Cost: Paid) <!-- include: diffToolList. path: /src/DiffEngine.Tests/diffToolList.include.md -->
 * **[P4Merge](/docs/diff-tool.md#p4merge)** Win/OSX/Linux (Cost: Free)
 * **[Kaleidoscope](/docs/diff-tool.md#kaleidoscope)** OSX (Cost: Paid)
 * **[DeltaWalker](/docs/diff-tool.md#deltawalker)** Win/OSX (Cost: Paid)
 * **[WinMerge](/docs/diff-tool.md#winmerge)** Win (Cost: Free with option to donate)
 * **[DiffMerge](/docs/diff-tool.md#diffmerge)** Win/OSX/Linux (Cost: Free)
 * **[TortoiseGitMerge](/docs/diff-tool.md#tortoisegitmerge)** Win (Cost: Free)
 * **[TortoiseGitIDiff](/docs/diff-tool.md#tortoisegitidiff)** Win (Cost: Free)
 * **[TortoiseMerge](/docs/diff-tool.md#tortoisemerge)** Win (Cost: Free)
 * **[TortoiseIDiff](/docs/diff-tool.md#tortoiseidiff)** Win (Cost: Free)
 * **[KDiff3](/docs/diff-tool.md#kdiff3)** Win/OSX (Cost: Free)
 * **[TkDiff](/docs/diff-tool.md#tkdiff)** OSX (Cost: Free)
 * **[Guiffy](/docs/diff-tool.md#guiffy)** Win/OSX (Cost: Paid)
 * **[ExamDiff](/docs/diff-tool.md#examdiff)** Win (Cost: Paid)
 * **[Diffinity](/docs/diff-tool.md#diffinity)** Win (Cost: Free with option to donate)
 * **[Rider](/docs/diff-tool.md#rider)** Win/OSX/Linux (Cost: Paid with free option for OSS)
 * **[Vim](/docs/diff-tool.md#vim)** Win/OSX (Cost: Free with option to donate)
 * **[Neovim](/docs/diff-tool.md#neovim)** Win/OSX/Linux (Cost: Free with option to sponsor)
 * **[AraxisMerge](/docs/diff-tool.md#araxismerge)** Win/OSX (Cost: Paid)
 * **[Meld](/docs/diff-tool.md#meld)** Win/OSX/Linux (Cost: Free)
 * **[SublimeMerge](/docs/diff-tool.md#sublimemerge)** Win/OSX/Linux (Cost: Paid)
 * **[VisualStudioCode](/docs/diff-tool.md#visualstudiocode)** Win/OSX/Linux (Cost: Free)
 * **[VisualStudio](/docs/diff-tool.md#visualstudio)** Win (Cost: Paid and free options) <!-- endInclude -->


## Launching a tool

A tool can be launched using the following:

<!-- snippet: DiffRunnerLaunch -->
<a id='snippet-diffrunnerlaunch'></a>
```cs
await DiffRunner.LaunchAsync(tempFile, targetFile);
```
<sup><a href='/src/DiffEngine.Tests/DiffRunnerTests.cs#L63-L67' title='Snippet source file'>snippet source</a> | <a href='#snippet-diffrunnerlaunch' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note that this method will respect the above [difference behavior](/docs/diff-tool.md#detected-difference-behavior) in terms of Auto refresh and MDI behaviors.


## Closing a tool

A tool can be closed using the following:

<!-- snippet: DiffRunnerKill -->
<a id='snippet-diffrunnerkill'></a>
```cs
DiffRunner.Kill(file1, file2);
```
<sup><a href='/src/DiffEngine.Tests/DiffRunnerTests.cs#L76-L80' title='Snippet source file'>snippet source</a> | <a href='#snippet-diffrunnerkill' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

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
 * [GoCD](https://docs.gocd.org/current/faq/dev_use_current_revision_in_build.html)


## Disable for a machine/process

Set an environment variable `DiffEngine_Disabled` with the value `true`.


## Disable in code

```
DiffRunner.Disabled = true;
```


## Icons

[Game](https://thenounproject.com/term/game/2956486/) designed by [Andrejs Kirma](https://thenounproject.com/andrejs/) from [The Noun Project](https://thenounproject.com).

Tray icons from [LineIcons](https://lineicons.com/icons/).
