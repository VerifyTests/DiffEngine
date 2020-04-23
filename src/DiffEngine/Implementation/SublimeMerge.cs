using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition SublimeMerge()
    {
        string Arguments(string tempFile, string target) =>
            $"mergetool \"{tempFile}\" \"{target}\"";

        return new Definition(
            name: DiffTool.SublimeMerge,
            url: "https://www.sublimemerge.com/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windows: new OsSettings(Arguments, @"%ProgramFiles%\Sublime Merge\smerge.exe"),
            linux: new OsSettings(Arguments, @"/usr/bin/smerge"),
            osx: new OsSettings(Arguments, @"/Applications/smerge.app/Contents/MacOS/smerge"),
            binaryExtensions: Array.Empty<string>());
    }
}