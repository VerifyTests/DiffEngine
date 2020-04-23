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
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windows:new OsSettings( BuildArguments,new[]
            {
                @"%ProgramFiles%\KDiff3\kdiff3.exe"
            }),
            linux: null,
            osx: new OsSettings(BuildArguments, new[]
            {
                "/Applications/kdiff3.app/Contents/MacOS/kdiff3"
            }),
            binaryExtensions: Array.Empty<string>(),
            notes: @"
 * `--cs CreateBakFiles=0` to not save a `.orig` file when merging");
    }
}