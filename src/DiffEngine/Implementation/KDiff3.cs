static partial class Implementation
{
    public static Definition KDiff3()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"\"{target}\" \"{temp}\" --cs CreateBakFiles=0",
            Right: (temp, target) => $"\"{temp}\" \"{target}\" --cs CreateBakFiles=0");

        return new(
            Tool: DiffTool.KDiff3,
            Url: "https://github.com/KDE/kdiff3",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: true,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions: [],
            OsSupport: new(
                Windows: new(
                    "kdiff3.exe",
                    launchArguments,
                    @"%ProgramFiles%\KDiff3\"),
                Osx: new(
                    "kdiff3",
                    launchArguments,
                    "/Applications/kdiff3.app/Contents/MacOS/")),
            Notes: " * `--cs CreateBakFiles=0` to not save a `.orig` file when merging");
    }
}