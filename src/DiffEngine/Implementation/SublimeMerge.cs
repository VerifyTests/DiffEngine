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
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windows: new OsSettings( BuildArguments,new[]
            {
                @"%ProgramFiles%\Sublime Merge\smerge.exe"
            }),
            linux: new OsSettings( BuildArguments,new[]
            {
                @"/usr/bin/smerge"
            }),
            osx: new OsSettings( BuildArguments, new[]
            {
                @"/Applications/smerge.app/Contents/MacOS/smerge"
            }),
            binaryExtensions: Array.Empty<string>());
    }
}