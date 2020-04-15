using System;
using System.IO;
using DiffEngine;

static partial class Implementation
{
    public static ToolDefinition WinMerge() =>
        new ToolDefinition(
            name: DiffTool.WinMerge,
            url: "https://winmerge.org/",
            supportsAutoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            buildArguments: (tempFile, targetFile, targetExists) =>
            {
                var leftDescription = Path.GetFileName(tempFile);
                var rightDescription = Path.GetFileName(targetFile);
                return $"/u /wl /e \"{tempFile}\" \"{targetFile}\" /dl \"{leftDescription}\" /dr \"{rightDescription}\"";
            },
            windowsExePaths: new[]
            {
                @"%ProgramFiles(x86)%\WinMerge\WinMergeU.exe"
            },
            binaryExtensions: Array.Empty<string>(),
            linuxExePaths: Array.Empty<string>(),
            osxExePaths: Array.Empty<string>(),
            notes: @"
 * [Command line reference](https://manual.winmerge.org/en/Command_line.html).
 * `/u` Prevents WinMerge from adding paths to the Most Recently Used (MRU) list.
 * `/wl` Opens the left side as read-only.
 * `/dl` and `/dr` Specifies file descriptions in the title bar.
 * `/e` Enables close with a single Esc key press.");
}