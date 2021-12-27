using DiffEngine;

static partial class Implementation
{
    public static Definition DeltaWalker()
    {
        static string TargetLeftArguments(string temp, string target) =>
            $"-mi \"{target}\" \"{temp}\"";

        static string TargetRightArguments(string temp, string target) =>
            $"-mi \"{temp}\" \"{target}\"";

        return new(
            name: DiffTool.DeltaWalker,
            url: "https://www.deltawalker.com/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: false,
            cost: "Paid",
            binaryExtensions: new[]
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
            osx: new(
                TargetLeftArguments,
                TargetRightArguments,
                "/Applications/DeltaWalker.app/Contents/MacOS/DeltaWalker"),
            windows: new(
                TargetLeftArguments,
                TargetRightArguments,
                @"C:\Program Files\Deltopia\DeltaWalker\DeltaWalker.exe"),
            notes: @"
 * [Command line usage](https://www.deltawalker.com/integrate/command-line)");
    }
}