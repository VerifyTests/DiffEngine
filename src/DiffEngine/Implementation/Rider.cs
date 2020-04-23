using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition Rider()
    {
        string Arguments(string temp, string target) =>
            $" diff \"{temp}\" \"{target}\"";

        return new Definition(
            name: DiffTool.Rider,
            url: "https://www.jetbrains.com/rider/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windows: new OsSettings(
                Arguments,
                @"%ProgramFiles%\JetBrains\JetBrains Rider *\bin\rider64.exe",
                @"%JetBrains Rider%\rider64.exe",
                @"%LOCALAPPDATA%\JetBrains\Toolbox\apps\Rider\*\*\bin\rider64.exe"),
            linux: null,
            osx: new OsSettings(
                Arguments,
                @"%HOME%/Library/Application Support/JetBrains/Toolbox/apps/Rider/*/*/Rider EAP.app/Contents/MacOS/rider",
                @"%HOME%/Library/Application Support/JetBrains/Toolbox/apps/Rider/*/*/Rider.app/Contents/MacOS/rider",
                @"/Applications/Rider EAP.app/Contents/MacOS/rider",
                @"/Applications/Rider.app/Contents/MacOS/rider"),
            binaryExtensions: Array.Empty<string>(),
            notes: @"
 * https://www.jetbrains.com/help/rider/Command_Line_Differences_Viewer.html");
    }
}