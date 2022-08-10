static partial class Implementation
{
    public static Definition P4MergeImage()
    {
        static string LeftArguments(string temp, string target)
        {
            return $"\"{target}\" \"{temp}\"";
        }

        static string RightArguments(string temp, string target)
        {
            return $"\"{temp}\" \"{target}\"";
        }

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