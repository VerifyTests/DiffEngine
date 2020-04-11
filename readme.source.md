# <img src="/src/icon.png" height="30px"> DiffEngine

[![Build status](https://ci.appveyor.com/api/projects/status/dpqylic0be7s9vnm/branch/master?svg=true)](https://ci.appveyor.com/project/SimonCropp/DiffEngine)
[![NuGet Status](https://img.shields.io/nuget/v/DiffEngine.svg)](https://www.nuget.org/packages/DiffEngine/)

DiffEngine manage launching and cleanup of diff tools. It is designed to be used by any Snapshot/Approval testing library.

Support is available via a [Tidelift Subscription](https://tidelift.com/subscription/pkg/nuget-diffengine?utm_source=nuget-diffengine&utm_medium=referral&utm_campaign=enterprise).

toc
include: doc-index


## NuGet package

 * https://nuget.org/packages/DiffEngine/


## [Supported Tools](/docs/diff-tool.md)

include: diffToolList


## Launching a tool

A tool can be launched using the following:

snippet: DiffRunnerLaunch

Note that this method will respect the above [difference behavior](#detected-difference-behavior) in terms of Auto refresh and MDI behaviors.


## Closing a tool

A tool can be closed using the following:

snippet: DiffRunnerKill

Note that this method will respect the above [difference behavior](#detected-difference-behavior) in terms of MDI behavior.


## File type detection

DiffEngine use [EmptyFiles](https://github.com/SimonCropp/EmptyFiles) to determineif a given file or extension is a binary or text.


## Security contact information

To report a security vulnerability, use the [Tidelift security contact](https://tidelift.com/security).Tidelift will coordinate the fix and disclosure.


## Icon

[Helmet](https://thenounproject.com/term/helmet/9554/) designed by [Leonidas Ikonomou](https://thenounproject.com/alterego) from [The Noun Project](https://thenounproject.com).