static partial class Implementation
{
    public static Definition TortoiseIDiff() =>
        new(
            Tool: DiffTool.TortoiseIDiff,
            Url: "https://tortoisesvn.net/TortoiseIDiff.html",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: false,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions:
            [
                "bmp",
                "gif",
                "ico",
                "jpg",
                "jpeg",
                "png",
                "tif",
                "tiff"
            ],
            OsSupport: new(
                Windows: new(
                    "TortoiseIDiff.exe",
                    new(
                        Left: (temp, target) => $"/left:\"{target}\" /right:\"{temp}\"",
                        Right: (temp, target) => $"/left:\"{temp}\" /right:\"{target}\""),
                    @"%ProgramFiles%\TortoiseSVN\bin\")));
}