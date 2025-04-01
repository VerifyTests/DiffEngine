static partial class Implementation
{
    public static Definition Kaleidoscope() =>
        new(
            Tool: DiffTool.Kaleidoscope,
            Url: "https://kaleidoscope.app",
            AutoRefresh: true,
            IsMdi: false,
            SupportsText: true,
            CreateNoWindow: false,
            RequiresTarget: true,
            Cost: "Paid",
            BinaryExtensions:
            [
                ".svg",
                ".bmp",
                ".gif",
                ".ico",
                ".jpg",
                ".jpeg",
                ".png",
                ".tiff",
                ".tif"
            ],
            OsSupport: new(
                Osx: new(
                    "ksdiff",
                    new(
                        Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                        Right: (temp, target) => $"\"{temp}\" \"{target}\""))));
}