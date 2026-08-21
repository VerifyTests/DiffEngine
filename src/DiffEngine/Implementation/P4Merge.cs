static partial class Implementation
{
    public static Definition P4Merge()
    {
        var launchArguments = new LaunchArguments(
            // p4merge takes the two files positionally as `left right`, so the target leads for
            // target-on-left and the temp file leads for target-on-right. Same order for text and
            // binary; only the encoding switch differs.
            Left: (temp, target) =>
            {
                if (FileExtensions.IsTextFile(temp))
                {
                    return $"-C utf8-bom \"{target}\" \"{temp}\"";
                }

                return $"\"{target}\" \"{temp}\"";
            },
            Right: (temp, target) =>
            {
                if (FileExtensions.IsTextFile(temp))
                {
                    return $"-C utf8-bom \"{temp}\" \"{target}\"";
                }

                return $"\"{temp}\" \"{target}\"";
            });

        return new(
            Tool: DiffTool.P4Merge,
            Url: "https://www.perforce.com/products/helix-core-apps/merge-diff-tool-p4merge",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: true,
            UseShellExecute: true,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions:
            [
                ".svg",
                ".bmp",
                ".gif",
                ".jpg",
                ".jpeg",
                ".png",
                ".pbm",
                ".pgm",
                ".ppm",
                ".tif",
                ".tiff",
                ".xbm",
                ".xpm"
            ],
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