using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition TkDiff()
    {
        return new(
            name: DiffTool.TkDiff,
            url: "https://sourceforge.net/projects/tkdiff/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free",
            binaryExtensions: Array.Empty<string>(),
            osx: new(
                (temp, target) => $"\"{temp}\" \"{target}\"",
                "/Applications/TkDiff.app/Contents/MacOS/tkdiff"));
    }
}