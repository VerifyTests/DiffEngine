using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition TortoiseGitMerge()
    {
        return new(
            name: DiffTool.TortoiseGitMerge,
            url: "https://tortoisegit.org/docs/tortoisegitmerge/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                (temp, target) => $"\"{temp}\" \"{target}\"",
                @"%ProgramFiles%\TortoiseGit\bin\TortoiseGitMerge.exe"));
    }
}