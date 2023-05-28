static partial class Implementation
{
    public static Definition DiffMerge()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"--nosplash \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"--nosplash \"{temp}\" \"{target}\"");

        var environmentVariable = $"${DefaultEnvironmentVariablePrefix}_{nameof(DiffTool.DiffMerge)}";
        return new(
            Tool: DiffTool.DiffMerge,
            Url: "https://www.sourcegear.com/diffmerge/",
            AutoRefresh: true,
            IsMdi: false,
            SupportsText: true,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions: Array.Empty<string>(),
            OsSupport: new(
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
                    "/Applications/DiffMerge.app/Contents/MacOS/")),
            Notes: " * Ensure [Check for Modified Files](https://www.sourcegear.com/diffmerge/webhelp/sec__opt__filewindows.html) is enabled");
    }
}