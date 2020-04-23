using DiffEngine;

static partial class Implementation
{
    public static Definition P4Merge()
    {
        string Arguments(string temp, string target) =>
            $"\"{temp}\" \"{target}\"";

        return new Definition(
            name: DiffTool.P4Merge,
            url: "https://www.perforce.com/products/helix-core-apps/merge-diff-tool-p4merge",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windows: new OsSettings(Arguments, @"%ProgramFiles%\Perforce\p4merge.exe"),
            linux: new OsSettings(Arguments, @"/usr/bin/p4merge"),
            osx: new OsSettings(Arguments, @"/Applications/p4merge.app/Contents/MacOS/p4merge"),
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
            });
    }
}