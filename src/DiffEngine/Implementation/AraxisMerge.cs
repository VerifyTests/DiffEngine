static partial class Implementation
{
    public static Definition AraxisMerge() =>
        new(
            name: DiffTool.AraxisMerge,
            url: "https://www.araxis.com/merge",
            autoRefresh: true,
            isMdi: true,
            cost: "Paid",
            supportsText: true,
            requiresTarget: true,
            binaryExtensions: new[]
            {
                "bmp",
                "dib",
                "emf",
                "gif",
                "jif",
                "j2c",
                "j2k",
                "jp2",
                "jpc",
                "jpeg",
                "jpg",
                "jpx",
                "pbm", //?
                "pcx",
                "pgm",
                "png",
                "ppm", //?
                "ras", //?
                "tif",
                "tiff",
                "tga",
                "wmf" //?
            },
            windows: new(
                "Compare.exe",
                (temp, target) => $"/nowait \"{target}\" \"{temp}\"",
                (temp, target) => $"/nowait \"{temp}\" \"{target}\"",
                @"%ProgramFiles%\Araxis\Araxis Merge"),
            osx: new(
                "compare",
                (temp, target) => $"-nowait \"{target}\" \"{temp}\"",
                (temp, target) => $"-nowait \"{temp}\" \"{target}\"",
                "/Applications/Araxis Merge.app/Contents/Utilities"),
            notes: @"
 * [Supported image files](https://www.araxis.com/merge/documentation-windows/comparing-image-files.en)
 * [Windows command line usage](https://www.araxis.com/merge/documentation-windows/command-line.en)
 * [MacOS command line usage](https://www.araxis.com/merge/documentation-os-x/command-line.en)
 * [Installing MacOS command line](https://www.araxis.com/merge/documentation-os-x/installing.en)");
    //TODO: add doco about auto refresh
}