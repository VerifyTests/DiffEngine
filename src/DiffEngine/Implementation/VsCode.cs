using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition VsCode()
    {
        static string Arguments(string temp, string target) =>
            $"--diff \"{target}\" \"{temp}\"";

        return new(
            name: DiffTool.VisualStudioCode,
            url: "https://code.visualstudio.com",
            autoRefresh: true,
            isMdi: true,
            supportsText: true,
            requiresTarget: true,
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                Arguments,
                @"%LocalAppData%\Programs\Microsoft VS Code\code.exe",
                @"%ProgramFiles%\Microsoft VS Code\bin\code.exe",
                @"%ProgramFiles%\Microsoft VS Code\code.exe"),
            linux: new(
                Arguments,
                "/usr/local/bin/code"),
            osx: new(
                Arguments,
                "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code"),
            notes: @"
 * [Command line reference](https://code.visualstudio.com/docs/editor/command-line)");
    }
}