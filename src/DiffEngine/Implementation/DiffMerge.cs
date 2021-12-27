using DiffEngine;

static partial class Implementation
{
    public static Definition DiffMerge()
    {
        static string TargetLeftArguments(string temp, string target) =>
            $"--nosplash \"{target}\" \"{temp}\"";

        static string TargetRightArguments(string temp, string target) =>
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
            windows: new(
                TargetLeftArguments,
                TargetRightArguments,
                @"%ProgramFiles%\SourceGear\Common\DiffMerge\sgdm.exe"),
            linux: new(
                TargetLeftArguments,
                TargetRightArguments,
                "/usr/bin/diffmerge"),
            osx: new(
                TargetLeftArguments,
                TargetRightArguments,
                "/Applications/DiffMerge.app/Contents/MacOS/DiffMerge"));
    }
}