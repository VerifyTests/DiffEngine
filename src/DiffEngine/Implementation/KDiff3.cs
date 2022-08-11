static partial class Implementation
{
    public static Definition KDiff3()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"\"{target}\" \"{temp}\" --cs CreateBakFiles=0",
            Right: (temp, target) => $"\"{temp}\" \"{target}\" --cs CreateBakFiles=0");

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
                launchArguments,
                @"%ProgramFiles%\KDiff3\"),
            osx: new(
                "kdiff3",
                launchArguments,
                "/Applications/kdiff3.app/Contents/MacOS/"),
            notes: @"
 * `--cs CreateBakFiles=0` to not save a `.orig` file when merging");
    }
}