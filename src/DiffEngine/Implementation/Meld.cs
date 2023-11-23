static partial class Implementation
{
    public static Definition Meld()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"\"{target}\" \"{temp}\"",
            Right: (temp, target) => $"\"{temp}\" \"{target}\"");

        return new(
            Tool: DiffTool.Meld,
            Url: "https://meldmerge.org/",
            AutoRefresh: false,
            IsMdi: true,
            SupportsText: true,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions: [],
            OsSupport: new(
                Windows: new(
                    "meld.exe",
                    launchArguments,
                    @"%LOCALAPPDATA%\Programs\Meld\",
                    @"%ProgramFiles%\Meld\"),
                Linux: new(
                    "meld",
                    launchArguments),
                Osx: new(
                    "meld",
                    launchArguments,
                    "/Applications/meld.app/Contents/MacOS/")),
            Notes: " * While Meld is not MDI, it is treated as MDI since it uses a single shared process to managing multiple windows. As such it is not possible to close a Meld merge process for a specific diff. [Vote for this feature](https://gitlab.gnome.org/GNOME/meld/-/issues/584)");
    }
}