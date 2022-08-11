static partial class Implementation
{
    public static Definition DiffMerge()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"--nosplash \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"--nosplash \"{temp}\" \"{target}\"");

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
                launchArguments,
                @"%ProgramFiles%\SourceGear\Common\DiffMerge\"),
            linux: new(
                "diffmerge",
                launchArguments),
            osx: new(
                "DiffMerge",
                launchArguments,
                "/Applications/DiffMerge.app/Contents/MacOS/"));
    }
}