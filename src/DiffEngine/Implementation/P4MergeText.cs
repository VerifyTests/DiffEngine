static partial class Implementation
{
    public static Definition P4MergeText()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"-C utf8-bom \"{temp}\" \"{target}\" \"{target}\" \"{target}\"",
            Right: (temp, target) => $"-C utf8-bom \"{target}\" \"{temp}\" \"{target}\" \"{target}\"");

        return new(
            Tool: DiffTool.P4MergeText,
            Url: "https://www.perforce.com/products/helix-core-apps/merge-diff-tool-p4merge",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: true,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions: Array.Empty<string>(),
            OsSupport: new(
            Windows: new(
                "p4merge.exe",
                launchArguments,
                @"%ProgramFiles%\Perforce\"),
            Linux: new(
                "p4merge",
                launchArguments),
            Osx: new(
                "p4merge",
                launchArguments,
                "/Applications/p4merge.app/Contents/MacOS/")));
    }
}