using DiffEngine;

static partial class Implementation
{
    public static Definition Meld()
    {
        static string TargetLeftArguments(string temp, string target) =>
            $"\"{target}\" \"{temp}\"";

        static string TargetRightArguments(string temp, string target) =>
            $"\"{temp}\" \"{target}\"";

        return new(
            name: DiffTool.Meld,
            url: "https://meldmerge.org/",
            autoRefresh: false,
            isMdi: true,
            supportsText: true,
            requiresTarget: true,
            cost: "Free",
            binaryExtensions: new[]
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
            windows: new(
                TargetLeftArguments,
                TargetRightArguments,
                @"%LOCALAPPDATA%\Programs\Meld\meld.exe",
                @"%ProgramFiles%\Meld\meld.exe"),
            linux: new(
                TargetLeftArguments,
                TargetRightArguments,
                "/usr/bin/meld"),
            osx: new(
                TargetLeftArguments,
                TargetRightArguments,
                "/Applications/meld.app/Contents/MacOS/meld"),
            notes: "While Meld is not MDI, it is treated as MDI since it uses a single shared process to managing multiple windows. As such it is not possible to close a Meld merge process for a specific diff. [Vote for this feature](https://gitlab.gnome.org/GNOME/meld/-/issues/584)");
    }
}