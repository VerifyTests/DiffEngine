using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition Neovim()
    {
        static string TargetLeftArguments(string temp, string target) => 
            $"-d \"{target}\" \"{temp}\"";

        static string TargetRightArguments(string temp, string target) => 
            $"-d \"{temp}\" \"{target}\"";

        return new(
            name: DiffTool.Neovim,
            url: "https://neovim.io/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free with option to sponsor",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                TargetLeftArguments,
                TargetRightArguments,
                @"%ChocolateyToolsLocation%\neovim\*\nvim.exe"),
            notes: @"
 * Assumes installed through Chocolatey https://chocolatey.org/packages/neovim/");
    }
}