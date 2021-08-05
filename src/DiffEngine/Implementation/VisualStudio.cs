using System;
using System.IO;
using DiffEngine;

static partial class Implementation
{
    public static Definition VisualStudio()
    {
        return new(
            name: DiffTool.VisualStudio,
            url: "https://docs.microsoft.com/en-us/visualstudio/ide/reference/diff",
            autoRefresh: true,
            isMdi: true,
            supportsText: true,
            requiresTarget: true,
            cost: "Paid and free options",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                (temp, target) =>
                {
                    var tempTitle = Path.GetFileName(temp);
                    var targetTitle = Path.GetFileName(target);
                    return $"/diff \"{temp}\" \"{target}\" \"{tempTitle}\" \"{targetTitle}\"";
                },
                @"%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Preview\Common7\IDE\devenv.exe",
                @"%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Community\Common7\IDE\devenv.exe",
                @"%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Professional\Common7\IDE\devenv.exe",
                @"%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\devenv.exe"));
    }
}