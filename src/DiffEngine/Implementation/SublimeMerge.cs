﻿static partial class Implementation
{
    public static Definition SublimeMerge()
    {
        static string LeftArguments(string temp, string target)
        {
            return $"mergetool \"{target}\" \"{temp}\"";
        }

        static string RightArguments(string temp, string target)
        {
            return $"mergetool \"{temp}\" \"{target}\"";
        }

        return new(
            name: DiffTool.SublimeMerge,
            url: "https://www.sublimemerge.com/",
            autoRefresh: false,
            isMdi: true,
            supportsText: true,
            requiresTarget: true,
            cost: "Paid",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                "smerge.exe",
                LeftArguments,
                RightArguments,
                @"%ProgramFiles%\Sublime Merge\"),
            linux: new(
                "smerge",
                LeftArguments,
                RightArguments),
            osx: new(
                "smerge",
                LeftArguments,
                RightArguments,
                "/Applications/smerge.app/Contents/MacOS/"),
            notes: "While SublimeMerge is not MDI, it is treated as MDI since it uses a single shared process to managing multiple windows. As such it is not possible to close a Sublime merge process for a specific diff. [Vote for this feature](https://github.com/sublimehq/sublime_merge/issues/1168)");
    }
}