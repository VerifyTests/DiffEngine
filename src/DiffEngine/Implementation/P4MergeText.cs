static partial class Implementation
{
    public static Definition P4MergeText()
    {
        static string TargetLeftArguments(string temp, string target)
        {
            return $"-C utf8-bom \"{temp}\" \"{target}\" \"{target}\" \"{target}\"";
        }

        static string TargetRightArguments(string temp, string target)
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
                TargetLeftArguments,
                TargetRightArguments,
                @"%ProgramFiles%\Perforce\p4merge.exe"),
            linux: new(
                TargetLeftArguments,
                TargetRightArguments,
                "/usr/bin/p4merge"),
            osx: new(
                TargetLeftArguments,
                TargetRightArguments,
                "/Applications/p4merge.app/Contents/MacOS/p4merge"));
    }
}