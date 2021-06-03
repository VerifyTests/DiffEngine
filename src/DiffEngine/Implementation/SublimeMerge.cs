using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition SublimeMerge()
    {
        static string Arguments(string tempFile, string target) =>
            $"mergetool \"{tempFile}\" \"{target}\"";

        return new(
            name: DiffTool.SublimeMerge,
            url: "https://www.sublimemerge.com/",
            autoRefresh: false,
            isMdi: true,
            supportsText: true,
            requiresTarget: true,
            binaryExtensions: Array.Empty<string>(),
            windows: new(Arguments, @"%ProgramFiles%\Sublime Merge\smerge.exe"),
            linux: new(Arguments, "/usr/bin/smerge"),
            osx: new(Arguments, "/Applications/smerge.app/Contents/MacOS/smerge"),
            notes: "While SublimeMerge is not MDI, it is treated as MDI since it uses a single shared process to managing multiple windows. As such it is not possible to close a Sublime merge process for a specific diff. [Vote for this feature](https://github.com/sublimehq/sublime_merge/issues/1168)");
    }
}