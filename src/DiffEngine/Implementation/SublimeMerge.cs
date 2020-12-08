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
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            binaryExtensions: Array.Empty<string>(),
            windows: new(Arguments, @"%ProgramFiles%\Sublime Merge\smerge.exe"),
            linux: new(Arguments, "/usr/bin/smerge"),
            osx: new(Arguments, "/Applications/smerge.app/Contents/MacOS/smerge"));
    }
}