using DiffEngine;

static partial class Implementation
{
    public static Definition TortoiseIDiff()
    {
        return new(
            name: DiffTool.TortoiseIDiff,
            url: "https://tortoisesvn.net/TortoiseIDiff.html",
            autoRefresh: false,
            isMdi: false,
            supportsText: false,
            requiresTarget: true,
            cost: "Free",
            binaryExtensions: new[]
            {
                "bmp",
                "gif",
                "ico",
                "jpg",
                "jpeg",
                "png",
                "tif",
                "tiff"
            },
            windows: new(
                (temp, target) => $"/left:\"{target}\" /right:\"{temp}\"",
                (temp, target) => $"/left:\"{temp}\" /right:\"{target}\"",
                @"%ProgramFiles%\TortoiseSVN\bin\TortoiseIDiff.exe"));
    }
}