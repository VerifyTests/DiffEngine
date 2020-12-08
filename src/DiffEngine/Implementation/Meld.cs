using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition Meld()
    {
        static string Arguments(string temp, string target) =>
            $"\"{temp}\" \"{target}\"";

        return new(
            name: DiffTool.Meld,
            url: "https://meldmerge.org/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            binaryExtensions: Array.Empty<string>(),
            windows: new(Arguments, @"%ProgramFiles%\Meld\meld.exe"),
            linux: new(Arguments, "/usr/bin/meld"),
            osx: new(Arguments, "/Applications/meld.app/Contents/MacOS/meld"));
    }
}