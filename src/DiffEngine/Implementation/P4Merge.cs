static partial class Implementation
{
    public static Definition P4Merge()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) =>
            {
                if (FileExtensions.IsText(temp))
                {
                    return $"\"{target}\" \"{temp}\"";
                }

                return $"-C utf8-bom \"{temp}\" \"{target}\" \"{target}\" \"{target}\"";
            },
            Right: (temp, target) =>
            {
                if (FileExtensions.IsText(temp))
                {
                    return $"\"{temp}\" \"{target}\"";
                }

                return $"-C utf8-bom \"{target}\" \"{temp}\" \"{target}\" \"{target}\"";
            });

        var environmentVariable = $"${DefaultEnvironmentVariablePrefix}_{nameof(DiffTool.P4Merge)}";
        return new(
            Tool: DiffTool.P4Merge,
            Url: "https://www.perforce.com/products/helix-core-apps/merge-diff-tool-p4merge",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: true,
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
                    environmentVariable,
                    "p4merge.exe",
                    launchArguments,
                    @"%ProgramFiles%\Perforce\"),
                Linux: new(
                    environmentVariable,
                    "p4merge",
                    launchArguments),
                Osx: new(
                    environmentVariable,
                    "p4merge",
                    launchArguments,
                    "/Applications/p4merge.app/Contents/MacOS/")));
    }
}