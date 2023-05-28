static partial class Implementation
{
    public static Definition TortoiseIDiff()
    {
        var environmentVariable = $"${DefaultEnvironmentVariablePrefix}_{nameof(DiffTool.TortoiseIDiff)}";
        return new(
            Tool: DiffTool.TortoiseIDiff,
            Url: "https://tortoisesvn.net/TortoiseIDiff.html",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: false,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions: new[]
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
            OsSupport: new(
                Windows: new(
                    environmentVariable,
                    "TortoiseIDiff.exe",
                    new(
                        Left: (temp, target) => $"/left:\"{target}\" /right:\"{temp}\"",
                        Right: (temp, target) => $"/left:\"{temp}\" /right:\"{target}\""),
                    @"%ProgramFiles%\TortoiseSVN\bin\")));
    }
}