using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition TortoiseGitMerge()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"\"{tempFile}\" \"{targetFile}\"";

        return new Definition(
            name: DiffTool.TortoiseGitMerge,
            url: "https://tortoisegit.org/docs/tortoisegitmerge/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windows: new OsSettings(BuildArguments,new[]
            {
                @"%ProgramFiles%\TortoiseGit\bin\TortoiseGitMerge.exe"
            }),
            linux: null,
            osx: null,
            binaryExtensions: Array.Empty<string>());
    }
}