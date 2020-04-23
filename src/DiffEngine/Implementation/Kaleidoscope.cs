using DiffEngine;

static partial class Implementation
{
    public static Definition Kaleidoscope()
    {
        return new Definition(
            name: DiffTool.Kaleidoscope,
            url: "https://www.kaleidoscopeapp.com/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windows: null,
            linux: null,
            osx: new OsSettings(
                (temp, target) => $"\"{temp}\" \"{target}\"",
                "/usr/local/bin/ksdiff"),
            binaryExtensions:
            new[]
            {
                "bmp",
                "gif",
                "ico",
                "jpg",
                "jpeg",
                "png",
                "tiff",
                "tif",
            });
    }
}