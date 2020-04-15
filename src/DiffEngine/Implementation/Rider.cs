using System;
using DiffEngine;

static partial class Implementation
{
    public static ToolDefinition Rider() =>
        new ToolDefinition(
            name: DiffTool.Rider,
            url: "https://www.jetbrains.com/rider/",
            supportsAutoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            buildArguments: (tempFile, targetFile) => $" diff \"{tempFile}\" \"{targetFile}\"",
            windowsExePaths: new[]
            {
                @"%ProgramFiles%\JetBrains\JetBrains Rider *\bin\rider64.exe",
                @"%JetBrains Rider%\rider64.exe",
                @"%LOCALAPPDATA%\JetBrains\Toolbox\apps\Rider\*\*\bin\rider64.exe"
            },
            binaryExtensions: Array.Empty<string>(),
            linuxExePaths: Array.Empty<string>(),
            osxExePaths: new[]
            {
                @"%HOME%/Library/Application Support/JetBrains/Toolbox/apps/Rider/*/*/Rider EAP.app/Contents/MacOS/rider",
                @"%HOME%/Library/Application Support/JetBrains/Toolbox/apps/Rider/*/*/Rider.app/Contents/MacOS/rider",
                @"/Applications/Rider EAP.app/Contents/MacOS/rider",
                @"/Applications/Rider.app/Contents/MacOS/rider"
            },
            notes: @"
 * https://www.jetbrains.com/help/rider/Command_Line_Differences_Viewer.html");
}