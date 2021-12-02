using DiffEngine;

static partial class Implementation
{
    public static Definition WinMerge()
    {
        static string TargetLeftArguments(string temp, string target)
        {
            var tempTitle = Path.GetFileName(temp);
            var targetTitle = Path.GetFileName(target);
            return $"/u /wl /e \"{target}\" \"{temp}\" /dl \"{targetTitle}\" /dr \"{tempTitle}\"";
        }

        static string TargetRightArguments(string temp, string target)
        {
            var tempTitle = Path.GetFileName(temp);
            var targetTitle = Path.GetFileName(target);
            return $"/u /wl /e \"{temp}\" \"{target}\" /dl \"{tempTitle}\" /dr \"{targetTitle}\"";
        }

        return new(
            name: DiffTool.WinMerge,
            url: "https://winmerge.org/",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            cost: "Free with option to donate",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                TargetLeftArguments,
                TargetRightArguments,
                @"%ProgramFiles%\WinMerge\WinMergeU.exe",
                @"%LocalAppData%\Programs\WinMerge\WinMergeU.exe"),
            notes: @"
 * [Command line reference](https://manual.winmerge.org/en/Command_line.html).
 * `/u` Prevents WinMerge from adding paths to the Most Recently Used (MRU) list.
 * `/wl` Opens the left side as read-only.
 * `/dl` and `/dr` Specifies file descriptions in the title bar.
 * `/e` Enables close with a single Esc key press.");
    }
}