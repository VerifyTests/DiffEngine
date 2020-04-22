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
            supportsAutoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windowsArguments: BuildArguments,
            linuxArguments: BuildArguments,
            osxArguments: BuildArguments,
            windowsPaths: new[]
            {
                @"%ProgramFiles%\TortoiseGit\bin\TortoiseGitMerge.exe"
            },
            binaryExtensions: Array.Empty<string>(),
            linuxPaths: Array.Empty<string>(),
            osxPaths: Array.Empty<string>());
    }
}