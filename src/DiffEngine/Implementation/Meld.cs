using System;
using DiffEngine;

static partial class Implementation
{
    public static ToolDefinition Meld()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"\"{tempFile}\" \"{targetFile}\"";

        return new ToolDefinition(
            name: DiffTool.Meld,
            url: "https://meldmerge.org/",
            supportsAutoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windowsArguments: BuildArguments,
            linuxArguments: BuildArguments,
            osxArguments: BuildArguments,
            windowsPaths: new[]
            {
                @"%ProgramFiles(x86)%\Meld\meld.exe"
            },
            binaryExtensions: Array.Empty<string>(),
            linuxPaths: new[]
            {
                @"/usr/bin/meld"
            },
            osxPaths: new[]
            {
                @"/Applications/meld.app/Contents/MacOS/meld"
            });
    }
}