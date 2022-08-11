static partial class Implementation
{
    public static Definition P4MergeImage()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"\"{target}\" \"{temp}\"",
            Right: (temp, target) => $"\"{temp}\" \"{target}\"");

        return new(
            Tool: DiffTool.P4MergeImage,
            Url: "https://www.perforce.com/products/helix-core-apps/merge-diff-tool-p4merge",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: false,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions: new[]
            {
                "bmp",
                "gif",
                "jpg",
                "jpeg",
                "png",
                "pbm",
                "pgm",
                "ppm",
                "tif",
                "tiff",
                "xbm",
                "xpm"
            },
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