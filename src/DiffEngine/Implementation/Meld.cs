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
            windows: new OsSettings(BuildArguments, new[]
            {
                @"%ProgramFiles(x86)%\Meld\meld.exe"
            }),
            linux: new OsSettings(BuildArguments, new[]
            {
                @"/usr/bin/meld"
            }),
            osx: new OsSettings(BuildArguments, new[]
            {
                @"/Applications/meld.app/Contents/MacOS/meld"
            }),
            binaryExtensions: Array.Empty<string>()
        );
    }
}