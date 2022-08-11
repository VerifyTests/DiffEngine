static partial class Implementation
{
    public static Definition DiffMerge()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"--nosplash \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"--nosplash \"{temp}\" \"{target}\"");

        return new(
            Tool: DiffTool.DiffMerge,
            Url: "https://www.sourcegear.com/diffmerge/",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: true,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions: Array.Empty<string>(),
            Windows: new(
                "sgdm.exe",
                launchArguments,
                @"%ProgramFiles%\SourceGear\Common\DiffMerge\"),
            Linux: new(
                "diffmerge",
                launchArguments),
            Osx: new(
                "DiffMerge",
                launchArguments,
                "/Applications/DiffMerge.app/Contents/MacOS/"));
    }
}