using System;
using DiffEngine;

static partial class Implementation
{
    public static ToolDefinition TkDiff()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"\"{tempFile}\" \"{targetFile}\"";

        return new ToolDefinition(
            name: DiffTool.TkDiff,
            url: "https://sourceforge.net/projects/tkdiff/",
            supportsAutoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windowsArguments: BuildArguments,
            linuxArguments: BuildArguments,
            osxArguments: BuildArguments,
            windowsExePaths: Array.Empty<string>(),
            binaryExtensions: Array.Empty<string>(),
            linuxExePaths: Array.Empty<string>(),
            osxExePaths: new[]
            {
                "/Applications/TkDiff.app/Contents/MacOS/tkdiff"
            });
    }
}