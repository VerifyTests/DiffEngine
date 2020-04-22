using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition TortoiseMerge()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"\"{tempFile}\" \"{targetFile}\"";

        return new Definition(
            name: DiffTool.TortoiseMerge,
            url: "https://tortoisesvn.net/TortoiseMerge.html",
            supportsAutoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windowsArguments: BuildArguments,
            linuxArguments: BuildArguments,
            osxArguments: BuildArguments,
            windowsPaths: new[]
            {
                @"%ProgramFiles%\TortoiseSVN\bin\TortoiseMerge.exe"
            },
            binaryExtensions: Array.Empty<string>(),
            linuxPaths: Array.Empty<string>(),
            osxPaths: Array.Empty<string>());
    }
}