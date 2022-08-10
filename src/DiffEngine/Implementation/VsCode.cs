﻿static partial class Implementation
{
    public static Definition VisualStudioCode()
    {
        static string TargetLeftArguments(string temp, string target)
        {
            return $"--diff \"{target}\" \"{temp}\"";
        }

        static string TargetRightArguments(string temp, string target)
        {
            return $"--diff \"{temp}\" \"{target}\"";
        }

        return new(
            name: DiffTool.VisualStudioCode,
            url: "https://code.visualstudio.com",
            autoRefresh: true,
            isMdi: true,
            supportsText: true,
            requiresTarget: true,
            cost: "Free",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                TargetLeftArguments,
                TargetRightArguments,
                @"%LocalAppData%\Programs\Microsoft VS Code\code.exe",
                @"%ProgramFiles%\Microsoft VS Code\code.exe",
                @"%UserProfile%\scoop\apps\vscode\current\code.exe"),
            linux: new(
                TargetLeftArguments,
                TargetRightArguments,
                "/usr/local/bin/code",
                "/usr/bin/code"),
            osx: new(
                TargetLeftArguments,
                TargetRightArguments,
                "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code"),
            notes: @"
 * [Command line reference](https://code.visualstudio.com/docs/editor/command-line)");
    }
}