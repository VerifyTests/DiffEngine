static partial class Implementation
{
    public static Definition BeyondCompare()
    {
        static string LeftWindowsArguments(string temp, string target)
        {
            return $"/solo /rightreadonly \"{target}\" \"{temp}\"";
        }

        static string RightWindowsArguments(string temp, string target)
        {
            return $"/solo /leftreadonly \"{temp}\" \"{target}\"";
        }

        static string LeftOsxLinuxArguments(string temp, string target)
        {
            return $"-solo -rightreadonly \"{target}\" \"{temp}\"";
        }

        static string RightOsxLinuxArguments(string temp, string target)
        {
            return $"-solo -leftreadonly \"{temp}\" \"{target}\"";
        }

        var environmentVariable = $"${DefaultEnvironmentVariablePrefix}_{nameof(DiffTool.BeyondCompare)}";
        return new(
            Tool: DiffTool.BeyondCompare,
            Url: "https://www.scootersoftware.com",
            AutoRefresh: true,
            IsMdi: false,
            SupportsText: true,
            Cost: "Paid",
            // technically BC doesnt require a target.
            // but if no target exists, the target cannot be edited
            RequiresTarget: true,
            BinaryExtensions: new[]
            {
                "pdf",
                "bmp",
                "gif",
                "ico",
                "jpg",
                "jpeg",
                "png",
                "tif",
                "tiff",
                "rtf"
            },
            OsSupport: new(
                Windows: new(
                    environmentVariable,
                    "BCompare.exe",
                    new(
                        LeftWindowsArguments,
                        RightWindowsArguments),
                    @"%ProgramFiles%\Beyond Compare *\"),
                Linux: new(
                    environmentVariable,
                    "bcomp",
                    new(
                        LeftOsxLinuxArguments,
                        RightOsxLinuxArguments),
                    "/usr/lib/beyondcompare/"),
                Osx: new(
                    environmentVariable,
                    "bcomp",
                    new(
                        LeftOsxLinuxArguments,
                        RightOsxLinuxArguments),
                    "/Applications/Beyond Compare.app/Contents/MacOS/")),
            Notes: @" * [Command line reference](https://www.scootersoftware.com/v4help/index.html?command_line_reference.html)");
    }
}