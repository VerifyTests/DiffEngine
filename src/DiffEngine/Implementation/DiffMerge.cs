using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition DiffMerge()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"--nosplash \"{tempFile}\" \"{targetFile}\"";

        return new Definition(
            name: DiffTool.DiffMerge,
            url: "https://www.sourcegear.com/diffmerge/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windows: new OsSettings(
                BuildArguments,
                new[]
                {
                    @"%ProgramFiles%\SourceGear\Common\DiffMerge\sgdm.exe"
                }),
            linux: new OsSettings(
                BuildArguments,
                new[]
                {
                    "/usr/bin/diffmerge"
                }),
            osx: new OsSettings(BuildArguments,
                new[]
                {
                    "/Applications/DiffMerge.app/Contents/MacOS/DiffMerge"
                }),
            binaryExtensions: Array.Empty<string>());
    }
}