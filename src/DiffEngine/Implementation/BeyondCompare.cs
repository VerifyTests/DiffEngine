static partial class Implementation
{
    public static Definition BeyondCompare()
    {
        static string LeftWindowsArguments(string temp, string target) =>
            $"/solo /rightreadonly /nobackups \"{target}\" \"{temp}\"";

        static string RightWindowsArguments(string temp, string target) =>
            $"/solo /leftreadonly /nobackups \"{temp}\" \"{target}\"";

        static string LeftOsxLinuxArguments(string temp, string target) =>
            $"-solo -rightreadonly -nobackups \"{target}\" \"{temp}\"";

        static string RightOsxLinuxArguments(string temp, string target) =>
            $"-solo -leftreadonly -nobackups \"{temp}\" \"{target}\"";

        return new(
            Tool: DiffTool.BeyondCompare,
            Url: "https://www.scootersoftware.com",
            AutoRefresh: true,
            IsMdi: false,
            SupportsText: true,
            CreateNoWindow: false,
            Cost: "Paid",
            // technically BC doesnt require a target.
            // but if no target exists, the target cannot be edited
            RequiresTarget: true,
            BinaryExtensions:
            [
                ".svg",
                ".pdf",
                ".bmp",
                ".gif",
                ".ico",
                ".jpg",
                ".jpeg",
                ".png",
                ".tif",
                ".tiff",
                ".rtf"
            ],
            OsSupport: new(
                Windows: new(
                    "BCompare.exe",
                    new(
                        LeftWindowsArguments,
                        RightWindowsArguments),
                    @"%ProgramFiles%\Beyond Compare *\",
                    @"%LOCALAPPDATA%\Programs\Beyond Compare *\"),
                Linux: new(
                    "bcompare",
                    new(
                        LeftOsxLinuxArguments,
                        RightOsxLinuxArguments),
                    "/usr/bin/"),
                Osx: new(
                    "bcomp",
                    new(
                        LeftOsxLinuxArguments,
                        RightOsxLinuxArguments),
                    "/Applications/Beyond Compare.app/Contents/MacOS/")),
            Notes: """
                    * [Command line reference](https://www.scootersoftware.com/v4help/index.html?command_line_reference.html)
                    * Enable [Automatically reload unless changes will be discarded](https://www.scootersoftware.com/v4help/optionstweak.html) in `Tools > Options > Tweaks > File Operations`.
                   """);
    }
}