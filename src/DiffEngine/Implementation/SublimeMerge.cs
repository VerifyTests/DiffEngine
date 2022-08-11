static partial class Implementation
{
    public static Definition SublimeMerge()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"mergetool \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"mergetool \"{temp}\" \"{target}\"");

        return new(
            name: DiffTool.SublimeMerge,
            url: "https://www.sublimemerge.com/",
            autoRefresh: false,
            isMdi: true,
            supportsText: true,
            requiresTarget: true,
            cost: "Paid",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                "smerge.exe",
                launchArguments,
                @"%ProgramFiles%\Sublime Merge\"),
            linux: new(
                "smerge",
                launchArguments),
            osx: new(
                "smerge",
                launchArguments,
                "/Applications/smerge.app/Contents/MacOS/"),
            notes: "While SublimeMerge is not MDI, it is treated as MDI since it uses a single shared process to managing multiple windows. As such it is not possible to close a Sublime merge process for a specific diff. [Vote for this feature](https://github.com/sublimehq/sublime_merge/issues/1168)");
    }
}