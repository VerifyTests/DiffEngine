static partial class Implementation
{
    public static Definition Kaleidoscope() =>
        new(
            name: DiffTool.Kaleidoscope,
            url: "https://www.kaleidoscopeapp.com/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Paid",
            binaryExtensions: new[]
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
            osx: new(
                "ksdiff",
                new(
                    Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                    Right: (temp, target) => $"\"{temp}\" \"{target}\"")));
}