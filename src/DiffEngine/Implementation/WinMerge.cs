static partial class Implementation
{
    public static Definition WinMerge()
    {
        static string LeftArguments(string temp, string target)
        {
            var tempTitle = Path.GetFileName(temp);
            var targetTitle = Path.GetFileName(target);
            return $"/u /wr /e \"{target}\" \"{temp}\" /dl \"{targetTitle}\" /dr \"{tempTitle}\" /cfg Backup/EnableFile=0";
        }

        static string RightArguments(string temp, string target)
        {
            var tempTitle = Path.GetFileName(temp);
            var targetTitle = Path.GetFileName(target);
            return $"/u /wl /e \"{temp}\" \"{target}\" /dl \"{tempTitle}\" /dr \"{targetTitle}\" /cfg Backup/EnableFile=0";
        }

        return new(
            Tool: DiffTool.WinMerge,
            Url: "https://winmerge.org/",
            AutoRefresh: true,
            IsMdi: false,
            SupportsText: true,
            RequiresTarget: true,
            Cost: "Free with option to donate",
            BinaryExtensions: new[]
            {
                "bmp",
                "cut",
                "dds",
                "exr",
                "g3",
                "gif",
                "hdr",
                "ico",
                "iff",
                "lbm",
                "j2k",
                "j2c",
                "jng",
                "jp2",
                "jpg",
                "jif",
                "jpeg",
                "jpe",
                "jxr",
                "wdp",
                "hdp",
                "koa",
                "mng",
                "pcd",
                "pcx",
                "pfm",
                "pct",
                "pict",
                "pic",
                "png",
                "pbm",
                "pgm",
                "ppm",
                "psd",
                "ras",
                "sgi",
                "rgb",
                "rgba",
                "bw",
                "tga",
                "targa",
                "tif",
                "tiff",
                "wap",
                "wbmp",
                "wbm",
                "webp",
                "xbm",
                "xpm"
            },
            OsSupport: new(
                Windows: new(
                    "WinMergeU.exe",
                    new(
                        LeftArguments,
                        RightArguments),
                    @"%ProgramFiles%\WinMerge\",
                    @"%LocalAppData%\Programs\WinMerge\")),
            Notes: """
                 * [Command line reference](https://manual.winmerge.org/en/Command_line.html).
                 * `/u` Prevents WinMerge from adding paths to the Most Recently Used (MRU) list.
                 * `/wl` Opens the left side as read-only.
                 * `/dl` and `/dr` Specifies file descriptions in the title bar.
                 * `/e` Enables close with a single Esc key press.
                 * `/cfg Backup/EnableFile=0` disable backup files.
                """);
    }
}