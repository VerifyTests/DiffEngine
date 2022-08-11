﻿static partial class Implementation
{
    public static Definition DeltaWalker()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"-mi \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"-mi \"{temp}\" \"{target}\"");

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
                "DeltaWalker",
                launchArguments,
                "/Applications/DeltaWalker.app/Contents/MacOS/"),
            windows: new(
                "DeltaWalker.exe",
                launchArguments,
                @"C:\Program Files\Deltopia\DeltaWalker\"),
            notes: @"
 * [Command line usage](https://www.deltawalker.com/integrate/command-line)");
    }
}