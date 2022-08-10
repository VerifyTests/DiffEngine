﻿static partial class Implementation
{
    public static Definition TortoiseGitMerge() =>
        new(
            name: DiffTool.TortoiseGitMerge,
            url: "https://tortoisegit.org/docs/tortoisegitmerge/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                "TortoiseGitMerge.exe",
                (temp, target) => $"\"{target}\" \"{temp}\"",
                (temp, target) => $"\"{temp}\" \"{target}\"",
                @"%ProgramFiles%\TortoiseGit\bin\"));
}