using DiffEngine;

static partial class Implementation
{
    public static Definition TortoiseIDiff()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"/left:\"{tempFile}\" /right:\"{targetFile}\"";

        return new Definition(
            name: DiffTool.TortoiseIDiff,
            url: "https://tortoisesvn.net/TortoiseIDiff.html",
            autoRefresh: false,
            isMdi: false,
            supportsText: false,
            requiresTarget: true,
            windows: new OsSettings(BuildArguments,new[]
            {
                @"%ProgramFiles%\TortoiseSVN\bin\TortoiseIDiff.exe"
            }),
            linux: null,
            osx: null,
            binaryExtensions: new[]
            {
                "bmp",
                "gif",
                "ico",
                "jpg",
                "jpeg",
                "png",
                "tif",
                "tiff",
            });
    }
}