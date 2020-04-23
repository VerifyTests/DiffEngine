using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition Meld()
    {
        string Arguments(string temp, string target) =>
            $"\"{temp}\" \"{target}\"";

        return new Definition(
            name: DiffTool.Meld,
            url: "https://meldmerge.org/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windows: new OsSettings(Arguments, @"%ProgramFiles(x86)%\Meld\meld.exe"),
            linux: new OsSettings(Arguments, @"/usr/bin/meld"),
            osx: new OsSettings(Arguments, @"/Applications/meld.app/Contents/MacOS/meld"),
            binaryExtensions: Array.Empty<string>()
        );
    }
}