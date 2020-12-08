using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition KDiff3()
    {
        static string Arguments(string temp, string target) =>
            $"\"{temp}\" \"{target}\" --cs CreateBakFiles=0";

        return new(
            name: DiffTool.KDiff3,
            url: "https://github.com/KDE/kdiff3",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            binaryExtensions: Array.Empty<string>(),
            windows: new(Arguments, @"%ProgramFiles%\KDiff3\kdiff3.exe"),
            osx: new(Arguments, "/Applications/kdiff3.app/Contents/MacOS/kdiff3"),
            notes: @"
 * `--cs CreateBakFiles=0` to not save a `.orig` file when merging");
    }
}