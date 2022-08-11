static partial class Implementation
{
    public static Definition P4MergeText()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"-C utf8-bom \"{temp}\" \"{target}\" \"{target}\" \"{target}\"",
            Right: (temp, target) => $"-C utf8-bom \"{target}\" \"{temp}\" \"{target}\" \"{target}\"");

        return new(
            name: DiffTool.P4MergeText,
            url: "https://www.perforce.com/products/helix-core-apps/merge-diff-tool-p4merge",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                "p4merge.exe",
                launchArguments,
                @"%ProgramFiles%\Perforce\"),
            linux: new(
                "p4merge",
                launchArguments),
            osx: new(
                "p4merge",
                launchArguments,
                "/Applications/p4merge.app/Contents/MacOS/"));
    }
}