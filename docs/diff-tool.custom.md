<!--
GENERATED FILE - DO NOT EDIT
This file was generated by [MarkdownSnippets](https://github.com/SimonCropp/MarkdownSnippets).
Source File: /docs/mdsource/diff-tool.custom.source.md
To change this file edit the source file and then run MarkdownSnippets.
-->

# Custom Diff Tool

A custom tool can be added by calling `DiffTools.AddTool`

<!-- snippet: AddTool -->
<a id='snippet-AddTool'></a>
```cs
var resolvedTool = DiffTools.AddTool(
    name: "MyCustomDiffTool",
    autoRefresh: true,
    isMdi: false,
    supportsText: true,
    requiresTarget: true,
    useShellExecute: true,
    launchArguments: new(
        Left: (tempFile, targetFile) => $"\"{targetFile}\" \"{tempFile}\"",
        Right: (tempFile, targetFile) => $"\"{tempFile}\" \"{targetFile}\""),
    exePath: diffToolPath,
    binaryExtensions: [".jpg"])!;
```
<sup><a href='/src/DiffEngine.Tests/DiffToolsTest.cs#L15-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-AddTool' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Add a tool based on existing resolved tool:

<!-- snippet: AddToolBasedOn -->
<a id='snippet-AddToolBasedOn'></a>
```cs
var resolvedTool = DiffTools.AddToolBasedOn(
    DiffTool.VisualStudio,
    name: "MyCustomDiffTool",
    launchArguments: new(
        Left: (temp, target) => $"\"custom args \"{target}\" \"{temp}\"",
        Right: (temp, target) => $"\"custom args \"{temp}\" \"{target}\""))!;
```
<sup><a href='/src/DiffEngine.Tests/DiffToolsTest.cs#L85-L94' title='Snippet source file'>snippet source</a> | <a href='#snippet-AddToolBasedOn' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## Resolution order

New tools are added to the top of the order, the last tool added will resolve before any existing tools. So when the following is executed the last tool that supports the file types will launch:

<!-- snippet: DiffRunnerLaunch -->
<a id='snippet-DiffRunnerLaunch'></a>
```cs
await DiffRunner.LaunchAsync(tempFile, targetFile);
```
<sup><a href='/src/DiffEngine.Tests/DiffRunnerTests.cs#L63-L67' title='Snippet source file'>snippet source</a> | <a href='#snippet-DiffRunnerLaunch' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Alternatively the instance  returned from `AddTool*` can be used to explicitly launch that tool.

<!-- snippet: AddToolAndLaunch -->
<a id='snippet-AddToolAndLaunch'></a>
```cs
var resolvedTool = DiffTools.AddToolBasedOn(
    DiffTool.VisualStudio,
    name: "MyCustomDiffTool",
    launchArguments: new(
        Left: (temp, target) => $"\"custom args \"{target}\" \"{temp}\"",
        Right: (temp, target) => $"\"custom args \"{temp}\" \"{target}\""));

await DiffRunner.LaunchAsync(resolvedTool!, "PathToTempFile", "PathToTargetFile");
```
<sup><a href='/src/DiffEngine.Tests/DiffToolsTest.cs#L105-L116' title='Snippet source file'>snippet source</a> | <a href='#snippet-AddToolAndLaunch' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## exePath

`exePath` is the path to the executable.

If the file cannot be found `AddTool*` will return null.


### Path conventions

 * [Environment.ExpandEnvironmentVariables](https://docs.microsoft.com/en-us/dotnet/api/system.environment.expandenvironmentvariables) is used to expand environment variables.
 * `*` can be used as a wildcard.
 * In the case where multiple matches are resolved the newest will be used.

So for example `%ProgramFiles(x86)%\Microsoft Visual Studio\*\*\Common7\IDE\devenv.exe` might discover the following:

 * `C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\Common7\IDE\devenv.exe`
 * `C:\Program Files (x86)\Microsoft Visual Studio\2017\Professional\Common7\IDE\devenv.exe`

Then `C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\Common7\IDE\devenv.exe` will be used.
