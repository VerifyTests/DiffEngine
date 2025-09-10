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
            UseShellExecute: true,
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
                ".rtf",
                ".7z",
                ".bz",
                ".bz2",
                ".tbz",
                ".tbz2",
                ".tbz2",
                ".chm",
                ".deb",
                ".img",
                ".iso",
                ".iso",
                ".gz",
                ".tgz",
                ".cab",
                ".rar",
                ".rpm",
                ".tar",
                ".wim",
                ".swm",
                ".xz",
                ".zip",
                ".zipx",
                ".jar",
                ".ear",
                ".war",
                ".bcpkg",
                ".nupkg",
                ".kmz"
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
                    * For `.kmz`, and `.nupkg` Beyond Compare needs to be configured to treat them as zip. 
                      `Tools > Options > Archive Types`. Scroll down to Zip, then add extra extension to the semicolon delimited list.
                   """);
    }
}