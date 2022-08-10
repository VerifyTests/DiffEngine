static partial class Implementation
{
    public static Definition Meld()
    {
        static string LeftArguments(string temp, string target)
        {
            return $"\"{target}\" \"{temp}\"";
        }

        static string RightArguments(string temp, string target)
        {
            return $"\"{temp}\" \"{target}\"";
        }

        return new(
            name: DiffTool.Meld,
            url: "https://meldmerge.org/",
            autoRefresh: false,
            isMdi: true,
            supportsText: true,
            requiresTarget: true,
            cost: "Free",
            binaryExtensions: Array.Empty<string>(),
            windows: new(
                "meld.exe",
                LeftArguments,
                RightArguments,
                @"%LOCALAPPDATA%\Programs\Meld\",
                @"%ProgramFiles%\Meld\"),
            linux: new(
                "meld",
                LeftArguments,
                RightArguments),
            osx: new(
                "meld",
                LeftArguments,
                RightArguments,
                "/Applications/meld.app/Contents/MacOS/"),
            notes: "While Meld is not MDI, it is treated as MDI since it uses a single shared process to managing multiple windows. As such it is not possible to close a Meld merge process for a specific diff. [Vote for this feature](https://gitlab.gnome.org/GNOME/meld/-/issues/584)");
    }
}