using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition CodeCompare()
    {
        return new Definition(
            name: DiffTool.CodeCompare,
            url: "https://www.devart.com/codecompare/",
            autoRefresh: false,
            isMdi: true,
            supportsText: true,
            requiresTarget: true,
            windows: new OsSettings(
                (temp, target) => $"\"{temp}\" \"{target}\"",
                @"%ProgramFiles%\Devart\Code Compare\CodeCompare.exe"),
            linux: null,
            osx: null,
            binaryExtensions: Array.Empty<string>(),
            notes: @"
 * [Command line reference](https://www.devart.com/codecompare/docs/index.html?comparing_via_command_line.htm)");
    }
}