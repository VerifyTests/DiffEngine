using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition KDiff3()
    {
        static string TargetLeftArguments(string temp, string target) =>
            $"\"{target}\" \"{temp}\" --cs CreateBakFiles=0";

        static string TargetRightArguments(string temp, string target) =>
            $"\"{temp}\" \"{target}\" --cs CreateBakFiles=0";

        return new(
            name: DiffTool.KDiff3,
            url: "https://github.com/KDE/kdiff3",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                TargetLeftArguments,
                TargetRightArguments, @"%ProgramFiles%\KDiff3\kdiff3.exe"),
            osx: new(
                TargetLeftArguments, 
                TargetRightArguments,
                "/Applications/kdiff3.app/Contents/MacOS/kdiff3"),
            notes: @"
 * `--cs CreateBakFiles=0` to not save a `.orig` file when merging");
    }
}