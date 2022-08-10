static partial class Implementation
{
    public static Definition P4MergeText()
    {
        static string LeftArguments(string temp, string target)
        {
            return $"-C utf8-bom \"{temp}\" \"{target}\" \"{target}\" \"{target}\"";
        }

        static string RightArguments(string temp, string target)
        {
            return $"-C utf8-bom \"{target}\" \"{temp}\" \"{target}\" \"{target}\"";
        }

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
                LeftArguments,
                RightArguments,
                @"%ProgramFiles%\Perforce\"),
            linux: new(
                "p4merge",
                LeftArguments,
                RightArguments),
            osx: new(
                "p4merge",
                LeftArguments,
                RightArguments,
                "/Applications/p4merge.app/Contents/MacOS/"));
    }
}