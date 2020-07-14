using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition Neovim()
    {
        string Arguments(string temp, string target)
        {
            return $"-d \"{temp}\" \"{target}\"";
        }

        return new Definition(
            name: DiffTool.Neovim,
            url: "https://neovim.io/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            shellExecute: true,
            requiresTarget: true,
            binaryExtensions: Array.Empty<string>(),
            windows: new OsSettings(Arguments, @"%ChocolateyToolsLocation%\neovim\*\nvim.exe"),
            notes: @"
 * Assumes installed through Chocolatey https://chocolatey.org/packages/neovim/");
    }
}