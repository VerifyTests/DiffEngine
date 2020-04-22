using System;
using DiffEngine;

static partial class Implementation
{
    public static ToolDefinition Kaleidoscope()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"\"{tempFile}\" \"{targetFile}\"";

        return new ToolDefinition(
            name: DiffTool.Kaleidoscope,
            url: "https://www.kaleidoscopeapp.com/",
            supportsAutoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            buildWindowsArguments: BuildArguments,
            buildLinuxArguments: BuildArguments,
            buildOsxArguments: BuildArguments,
            windowsExePaths: Array.Empty<string>(),
            binaryExtensions: new[]
            {
                "bmp",
                "gif",
                "ico",
                "jpg",
                "jpeg",
                "png",
                "tiff",
                "tif",
            },
            linuxExePaths: Array.Empty<string>(),
            osxExePaths: new[]
            {
                "/usr/local/bin/ksdiff"
            });
    }
}