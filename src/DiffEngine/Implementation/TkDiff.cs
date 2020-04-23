using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition TkDiff()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"\"{tempFile}\" \"{targetFile}\"";

        return new Definition(
            name: DiffTool.TkDiff,
            url: "https://sourceforge.net/projects/tkdiff/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windows: null,
            linux: null,
            osx: new OsSettings(BuildArguments, new[]
            {
                "/Applications/TkDiff.app/Contents/MacOS/tkdiff"
            }),
            binaryExtensions: Array.Empty<string>());
    }
}