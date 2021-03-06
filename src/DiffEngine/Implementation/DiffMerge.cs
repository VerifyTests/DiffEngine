﻿using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition DiffMerge()
    {
        static string Arguments(string temp, string target) =>
            $"--nosplash \"{temp}\" \"{target}\"";

        return new(
            name: DiffTool.DiffMerge,
            url: "https://www.sourcegear.com/diffmerge/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free",
            binaryExtensions: Array.Empty<string>(),
            windows: new(Arguments, @"%ProgramFiles%\SourceGear\Common\DiffMerge\sgdm.exe"),
            linux: new(Arguments, "/usr/bin/diffmerge"),
            osx: new(Arguments, "/Applications/DiffMerge.app/Contents/MacOS/DiffMerge"));
    }
}