static partial class Implementation
{
    public static Definition DiffMerge()
    {
        static string TargetLeftArguments(string temp, string target)
        {
            return $"--nosplash \"{target}\" \"{temp}\"";
        }

        static string TargetRightArguments(string temp, string target)
        {
            return $"--nosplash \"{temp}\" \"{target}\"";
        }

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
                "sgdm.exe",
                TargetLeftArguments,
                TargetRightArguments,
                @"%ProgramFiles%\SourceGear\Common\DiffMerge\"),
            linux: new(
                "diffmerge",
                TargetLeftArguments,
                TargetRightArguments,
                "/usr/bin/"),
            osx: new(
                "DiffMerge",
                TargetLeftArguments,
                TargetRightArguments,
                "/Applications/DiffMerge.app/Contents/MacOS/"));
    }
}