using DiffEngine;

static partial class Implementation
{
    public static Definition P4Merge()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"\"{tempFile}\" \"{targetFile}\"";

        return new Definition(
            name: DiffTool.P4Merge,
            url: "https://www.perforce.com/products/helix-core-apps/merge-diff-tool-p4merge",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windows: new OsSettings(BuildArguments, new[]
            {
                @"%ProgramFiles%\Perforce\p4merge.exe"
            }),
            linux: new OsSettings(BuildArguments, new[]
            {
                @"/usr/bin/p4merge"
            }),
            osx: new OsSettings(BuildArguments, new[]
            {
                @"/Applications/p4merge.app/Contents/MacOS/p4merge"
            }),
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