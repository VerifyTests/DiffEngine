using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition KDiff3()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"\"{tempFile}\" \"{targetFile}\" --cs CreateBakFiles=0";

        return new Definition(
            name: DiffTool.KDiff3,
            url: "https://github.com/KDE/kdiff3",
            supportsAutoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windowsArguments: BuildArguments,
            linuxArguments: BuildArguments,
            osxArguments: BuildArguments,
            windowsPaths: new[]
            {
                @"%ProgramFiles%\KDiff3\kdiff3.exe"
            },
            binaryExtensions: Array.Empty<string>(),
            linuxPaths: Array.Empty<string>(),
            osxPaths: new[]
            {
                "/Applications/kdiff3.app/Contents/MacOS/kdiff3"
            },
            notes: @"
 * `--cs CreateBakFiles=0` to not save a `.orig` file when merging");
    }
}