using System;
using DiffEngine;

static partial class Implementation
{
    public static ToolDefinition SublimeMerge()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"mergetool \"{tempFile}\" \"{targetFile}\"";

        return new ToolDefinition(
            name: DiffTool.SublimeMerge,
            url: "https://www.sublimemerge.com/",
            supportsAutoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            buildWindowsArguments: BuildArguments,
            buildLinuxArguments: BuildArguments,
            buildOsxArguments: BuildArguments,
            windowsExePaths: new[]
            {
                @"%ProgramFiles%\Sublime Merge\smerge.exe"
            },
            binaryExtensions: Array.Empty<string>(),
            linuxExePaths: new[]
            {
                @"/usr/bin/smerge"
            },
            osxExePaths: new[]
            {
                @"/Applications/smerge.app/Contents/MacOS/smerge"
            });
    }
}