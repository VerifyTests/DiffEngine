static partial class Implementation
{
    public static Definition DeltaWalker()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"-mi \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"-mi \"{temp}\" \"{target}\"");

        var environmentVariable = $"${DefaultEnvironmentVariablePrefix}_{nameof(DiffTool.DeltaWalker)}";

        return new(
            Tool: DiffTool.DeltaWalker,
            Url: "https://www.deltawalker.com/",
            AutoRefresh: true,
            IsMdi: false,
            SupportsText: true,
            RequiresTarget: false,
            Cost: "Paid",
            BinaryExtensions: new[]
            {
                "jpg",
                "jp2",
                "j2k",
                "png",
                "gif",
                "psd",
                "tif",
                "bmp",
                "pct",
                "pict",
                "pic",
                "ico",
                "ppm",
                "pgm",
                "pbm",
                "pnm",
                "zip",
                "jar",
                "ear",
                "tar",
                "tgz",
                "tbz2",
                "gz",
                "bz2",
                "doc",
                "docx",
                "xls",
                "xlsx",
                "ppt",
                "pdf",
                "rtf",
                "html",
                "htm"
            },
            OsSupport: new(
                Osx: new(
                    environmentVariable,
                    "DeltaWalker",
                    launchArguments,
                    "/Applications/DeltaWalker.app/Contents/MacOS/"),
                Windows: new(
                    environmentVariable,
                    "DeltaWalker.exe",
                    launchArguments,
                    @"C:\Program Files\Deltopia\DeltaWalker\")),
            Notes: @" * [Command line usage](https://www.deltawalker.com/integrate/command-line)");
    }
}