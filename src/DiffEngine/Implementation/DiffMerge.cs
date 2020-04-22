using System;
using DiffEngine;

static partial class Implementation
{
    public static ToolDefinition DiffMerge()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"--nosplash \"{tempFile}\" \"{targetFile}\"";

        return new ToolDefinition(
            name: DiffTool.DiffMerge,
            url: "https://www.sourcegear.com/diffmerge/",
            supportsAutoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windowsArguments: BuildArguments,
            linuxArguments: BuildArguments,
            osxArguments: BuildArguments,
            windowsPaths: new[]
            {
                @"%ProgramFiles%\SourceGear\Common\DiffMerge\sgdm.exe"
            },
            binaryExtensions: Array.Empty<string>(),
            linuxPaths: new[]
            {
                "/usr/bin/diffmerge"
            },
            osxPaths: new[]
            {
                "/Applications/DiffMerge.app/Contents/MacOS/DiffMerge"
            });
    }
}