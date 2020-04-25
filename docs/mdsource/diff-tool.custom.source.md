# Custom Diff Tool

A custom tool can be added by calling `DiffTools.AddTool`

snippet: AddTool

Add a tool based on existing resolved tool:

snippet: AddToolBasedOn


## exePath

`exePath` is the path to the executable.

If the file cannot be found `AddTool*` will return null.


### Path conventions

 * `Environment.ExpandEnvironmentVariables` is used to expand environment variables.
 * `*` can be used as a wildcard.
 * In the case where multiple matches are resolved the newest will be used.

So for example `%ProgramFiles(x86)%\Microsoft Visual Studio\*\*\Common7\IDE\devenv.exe` might discover the following:

 * `C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\Common7\IDE\devenv.exe`
 * `C:\Program Files (x86)\Microsoft Visual Studio\2017\Professional\Common7\IDE\devenv.exe`

Then `C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\Common7\IDE\devenv.exe` will be used.