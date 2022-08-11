static partial class Implementation
{
    public static Definition TortoiseIDiff() =>
        new(
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
                "TortoiseIDiff.exe",
                new(
                    Left: (temp, target) => $"/left:\"{target}\" /right:\"{temp}\"",
                    Right: (temp, target) => $"/left:\"{temp}\" /right:\"{target}\""),
                @"%ProgramFiles%\TortoiseSVN\bin\"));
}