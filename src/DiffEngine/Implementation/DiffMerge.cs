using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition DiffMerge()
    {
        static string Arguments(string temp, string target) =>
            $"--nosplash \"{temp}\" \"{target}\"";

        return new Definition(
            name: DiffTool.DiffMerge,
            url: "https://www.sourcegear.com/diffmerge/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            binaryExtensions: Array.Empty<string>(),
            windows: new OsSettings(Arguments, @"%ProgramFiles%\SourceGear\Common\DiffMerge\sgdm.exe"),
            linux: new OsSettings(Arguments, "/usr/bin/diffmerge"),
            osx: new OsSettings(Arguments, "/Applications/DiffMerge.app/Contents/MacOS/DiffMerge"));
    }
}