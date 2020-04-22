using System;
using System.IO;
using DiffEngine;

static partial class Implementation
{
    public static Definition WinMerge()
    {
        string BuildArguments(string tempFile, string targetFile)
        {
            var leftTitle = Path.GetFileName(tempFile);
            var rightTitle = Path.GetFileName(targetFile);
            return $"/u /wl /e \"{tempFile}\" \"{targetFile}\" /dl \"{leftTitle}\" /dr \"{rightTitle}\"";
        }

        return new Definition(
            name: DiffTool.WinMerge,
            url: "https://winmerge.org/",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windowsArguments: BuildArguments,
            linuxArguments: BuildArguments,
            osxArguments: BuildArguments,
            windowsPaths: new[]
            {
                @"%ProgramFiles(x86)%\WinMerge\WinMergeU.exe"
            },
            binaryExtensions: Array.Empty<string>(),
            linuxPaths: Array.Empty<string>(),
            osxPaths: Array.Empty<string>(),
            notes: @"
 * [Command line reference](https://manual.winmerge.org/en/Command_line.html).
 * `/u` Prevents WinMerge from adding paths to the Most Recently Used (MRU) list.
 * `/wl` Opens the left side as read-only.
 * `/dl` and `/dr` Specifies file descriptions in the title bar.
 * `/e` Enables close with a single Esc key press.");
    }
}