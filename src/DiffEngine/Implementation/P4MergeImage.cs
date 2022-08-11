static partial class Implementation
{
    public static Definition P4MergeImage()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"\"{target}\" \"{temp}\"",
            Right: (temp, target) => $"\"{temp}\" \"{target}\"");

        return new(
            name: DiffTool.P4MergeImage,
            url: "https://www.perforce.com/products/helix-core-apps/merge-diff-tool-p4merge",
            autoRefresh: false,
            isMdi: false,
            supportsText: false,
            requiresTarget: true,
            cost: "Free",
            binaryExtensions: new[]
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