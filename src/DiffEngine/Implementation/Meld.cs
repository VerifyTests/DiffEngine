using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition Meld()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"\"{tempFile}\" \"{targetFile}\"";

        return new Definition(
            name: DiffTool.Meld,
            url: "https://meldmerge.org/",
            autoRefresh: false,
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