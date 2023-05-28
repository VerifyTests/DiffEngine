static partial class Implementation
{
    public static Definition Meld()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"\"{target}\" \"{temp}\"",
            Right: (temp, target) => $"\"{temp}\" \"{target}\"");

        var environmentVariable = $"${DefaultEnvironmentVariablePrefix}_{nameof(DiffTool.Meld)}";
        return new(
            Tool: DiffTool.Meld,
            Url: "https://meldmerge.org/",
            AutoRefresh: false,
            IsMdi: true,
            SupportsText: true,
            RequiresTarget: true,
            Cost: "Free",
            BinaryExtensions: Array.Empty<string>(),
            OsSupport: new(
                Windows: new(
                    environmentVariable,
                    "meld.exe",
                    launchArguments,
                    @"%LOCALAPPDATA%\Programs\Meld\",
                    @"%ProgramFiles%\Meld\"),
                Linux: new(
                    environmentVariable,
                    "meld",
                    launchArguments),
                Osx: new(
                    environmentVariable,
                    "meld",
                    launchArguments,
                    "/Applications/meld.app/Contents/MacOS/")),
            Notes: " * While Meld is not MDI, it is treated as MDI since it uses a single shared process to managing multiple windows. As such it is not possible to close a Meld merge process for a specific diff. [Vote for this feature](https://gitlab.gnome.org/GNOME/meld/-/issues/584)");
    }
}