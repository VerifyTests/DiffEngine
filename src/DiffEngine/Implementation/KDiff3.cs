static partial class Implementation
{
    public static Definition KDiff3()
    {
        static string LeftArguments(string temp, string target)
        {
            return $"\"{target}\" \"{temp}\" --cs CreateBakFiles=0";
        }

        static string RightArguments(string temp, string target)
        {
            return $"\"{temp}\" \"{target}\" --cs CreateBakFiles=0";
        }

        return new(
            name: DiffTool.KDiff3,
            url: "https://github.com/KDE/kdiff3",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                "kdiff3.exe",
                LeftArguments,
                RightArguments,
                @"%ProgramFiles%\KDiff3\"),
            osx: new(
                "kdiff3",
                LeftArguments,
                RightArguments,
                "/Applications/kdiff3.app/Contents/MacOS/"),
            notes: @"
 * `--cs CreateBakFiles=0` to not save a `.orig` file when merging");
    }
}