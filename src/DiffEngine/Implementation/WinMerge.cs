﻿using System;
using System.IO;
using DiffEngine;

static partial class Implementation
{
    public static Definition WinMerge()
    {
        return new(
            name: DiffTool.WinMerge,
            url: "https://winmerge.org/",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free with option to donate",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                (temp, target) =>
                {
                    var leftTitle = Path.GetFileName(temp);
                    var rightTitle = Path.GetFileName(target);
                    return $"/u /wl /e \"{temp}\" \"{target}\" /dl \"{leftTitle}\" /dr \"{rightTitle}\"";
                },
                @"%ProgramFiles%\WinMerge\WinMergeU.exe"),
            notes: @"
 * [Command line reference](https://manual.winmerge.org/en/Command_line.html).
 * `/u` Prevents WinMerge from adding paths to the Most Recently Used (MRU) list.
 * `/wl` Opens the left side as read-only.
 * `/dl` and `/dr` Specifies file descriptions in the title bar.
 * `/e` Enables close with a single Esc key press.");
    }
}