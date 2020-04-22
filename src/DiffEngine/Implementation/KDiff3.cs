using System;
using DiffEngine;

static partial class Implementation
{
    public static ToolDefinition KDiff3()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"\"{tempFile}\" \"{targetFile}\" --cs CreateBakFiles=0";

        return new ToolDefinition(
            name: DiffTool.KDiff3,
            url: "https://github.com/KDE/kdiff3",
            supportsAutoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            buildWindowsArguments: BuildArguments,
            buildLinuxArguments: BuildArguments,
            buildOsxArguments: BuildArguments,
            windowsExePaths: new[]
            {
                @"%ProgramFiles%\KDiff3\kdiff3.exe"
            },
            binaryExtensions: Array.Empty<string>(),
            linuxExePaths: Array.Empty<string>(),
            osxExePaths: new[]
            {
                "/Applications/kdiff3.app/Contents/MacOS/kdiff3"
            },
            notes: @"
 * `--cs CreateBakFiles=0` to not save a `.orig` file when merging");
    }
}