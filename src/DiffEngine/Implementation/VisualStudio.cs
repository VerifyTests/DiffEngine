using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition VisualStudio()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"/diff \"{targetFile}\" \"{tempFile}\"";

        return new Definition(
            name: DiffTool.VisualStudio,
            url: "https://docs.microsoft.com/en-us/visualstudio/ide/reference/diff",
            autoRefresh: true,
            isMdi: true,
            supportsText: true,
            requiresTarget: true,
            windowsArguments: BuildArguments,
            linuxArguments: BuildArguments,
            osxArguments: BuildArguments,
            windowsPaths: new[]
            {
                @"%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Preview\Common7\IDE\devenv.exe",
                @"%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Community\Common7\IDE\devenv.exe",
                @"%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Professional\Common7\IDE\devenv.exe",
                @"%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\devenv.exe",
            },
            binaryExtensions: Array.Empty<string>(),
            linuxPaths: Array.Empty<string>(),
            osxPaths: Array.Empty<string>());
    }
}