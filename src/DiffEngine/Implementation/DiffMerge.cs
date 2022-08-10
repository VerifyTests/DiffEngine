static partial class Implementation
{
    public static Definition DiffMerge()
    {
        static string LeftArguments(string temp, string target)
        {
            return $"--nosplash \"{target}\" \"{temp}\"";
        }

        static string RightArguments(string temp, string target)
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
                LeftArguments,
                RightArguments,
                @"%ProgramFiles%\SourceGear\Common\DiffMerge\"),
            linux: new(
                "diffmerge",
                LeftArguments,
                RightArguments),
            osx: new(
                "DiffMerge",
                LeftArguments,
                RightArguments,
                "/Applications/DiffMerge.app/Contents/MacOS/"));
    }
}