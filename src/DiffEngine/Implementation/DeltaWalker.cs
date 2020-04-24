using DiffEngine;

static partial class Implementation
{
    public static Definition DeltaWalker() =>
        new Definition(
            name: DiffTool.DeltaWalker,
            url: "https://www.deltawalker.com/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: false,
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
            osx: new OsSettings(
                (temp, target) => $"-mi \"{temp}\" \"{target}\"",
                "/Applications/DeltaWalker.app/Contents/MacOS/DeltaWalker"),
            notes: @"
 * [Command line usage](https://www.deltawalker.com/integrate/command-line)");
}