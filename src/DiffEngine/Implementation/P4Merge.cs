using DiffEngine;

static partial class Implementation
{
    public static Definition P4Merge()
    {
        static string Arguments(string temp, string target) =>
            $" -C utf8-bom \"{target}\" \"{temp}\" \"{target}\" \"{target}\"";

        return new(
            name: DiffTool.P4Merge,
            url: "https://www.perforce.com/products/helix-core-apps/merge-diff-tool-p4merge",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
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
            windows: new(Arguments, @"%ProgramFiles%\Perforce\p4merge.exe"),
            linux: new(Arguments, "/usr/bin/p4merge"),
            osx: new(Arguments, "/Applications/p4merge.app/Contents/MacOS/p4merge"));
    }
}