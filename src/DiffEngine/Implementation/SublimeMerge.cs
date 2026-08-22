static partial class Implementation
{
    public static Definition SublimeMerge()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"mergetool \"{target}\" \"{temp}\"",
            Right: (temp, target) => $"mergetool \"{temp}\" \"{target}\"");

        return new(
            Tool: DiffTool.SublimeMerge,
            Url: "https://www.sublimemerge.com/",
            AutoRefresh: false,
            IsMdi: true,
            SupportsText: true,
            UseShellExecute: true,
            RequiresTarget: true,
            Cost: "Paid",
            BinaryExtensions: [".svg"],
            OsSupport: new(
                Windows: new(
                    "smerge.exe",
                    launchArguments,
                    @"%ProgramFiles%\Sublime Merge\"),
                Linux: new(
                    "smerge",
                    launchArguments),
                Osx: new(
                    "smerge",
                    launchArguments,
                    // The bundle is "Sublime Merge.app", and the command line tool inside it lives
                    // under SharedSupport/bin rather than MacOS - which holds the GUI binary. The
                    // one path listed here named neither, so a Mac install was never found by
                    // directory search and only resolved for someone who had put smerge on PATH
                    // themselves
                    "/Applications/Sublime Merge.app/Contents/SharedSupport/bin/",
                    "/Applications/smerge.app/Contents/MacOS/")),
            Notes: " * While SublimeMerge is not MDI, it is treated as MDI since it uses a single shared process to managing multiple windows. As such it is not possible to close a Sublime merge process for a specific diff. [Vote for this feature](https://github.com/sublimehq/sublime_merge/issues/1168)");
    }
}