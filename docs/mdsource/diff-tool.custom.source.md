# Custom Diff Tool

A custom tool can be added by calling `DiffTools.AddTool`

snippet: AddTool

Add a tool based on existing resolved tool:

snippet: AddToolBasedOn


## Resolution order

New tools are added to the top of the order, the last tool added will resolve before any existing tools. So when the following is executed the last tool that supports the file types will launch:

snippet: DiffRunnerLaunch

Alternatively the instance  returned from `AddTool*` can be used to explicitly launch that tool.

snippet: AddToolAndLaunch


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