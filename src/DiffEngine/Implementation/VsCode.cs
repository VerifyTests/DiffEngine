using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition VsCode()
    {
        string Arguments(string temp, string target) =>
            $"--diff \"{target}\" \"{temp}\"";

        return new Definition(
            name: DiffTool.VisualStudioCode,
            url: "https://code.visualstudio.com",
            autoRefresh: true,
            isMdi: true,
            supportsText: true,
            requiresTarget: true,
            windows: new OsSettings(
                Arguments,
                @"%LocalAppData%\Programs\Microsoft VS Code\code.exe",
                @"%ProgramFiles%\Microsoft VS Code\bin\code"),
            linux: new OsSettings(
                Arguments,
                @"/usr/local/bin/code"),
            osx: new OsSettings(
                Arguments,
                "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code"),
            binaryExtensions: Array.Empty<string>(),
            notes: @"
 * [Command line reference](https://code.visualstudio.com/docs/editor/command-line)");
    }
}