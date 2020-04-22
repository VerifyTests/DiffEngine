using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition SublimeMerge()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"mergetool \"{tempFile}\" \"{targetFile}\"";

        return new Definition(
            name: DiffTool.SublimeMerge,
            url: "https://www.sublimemerge.com/",
            supportsAutoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windowsArguments: BuildArguments,
            linuxArguments: BuildArguments,
            osxArguments: BuildArguments,
            windowsPaths: new[]
            {
                @"%ProgramFiles%\Sublime Merge\smerge.exe"
            },
            binaryExtensions: Array.Empty<string>(),
            linuxPaths: new[]
            {
                @"/usr/bin/smerge"
            },
            osxPaths: new[]
            {
                @"/Applications/smerge.app/Contents/MacOS/smerge"
            });
    }
}