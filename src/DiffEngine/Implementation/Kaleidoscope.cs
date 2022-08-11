static partial class Implementation
{
    public static Definition Kaleidoscope() =>
        new(
            Tool: DiffTool.Kaleidoscope,
            Url: "https://www.kaleidoscopeapp.com/",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: true,
            RequiresTarget: true,
            Cost: "Paid",
            BinaryExtensions: new[]
            {
                "bmp",
                "gif",
                "ico",
                "jpg",
                "jpeg",
                "png",
                "tiff",
                "tif"
            },
            OsSupport: new(
                Osx: new(
                    "ksdiff",
                    new(
                        Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                        Right: (temp, target) => $"\"{temp}\" \"{target}\""))));
}