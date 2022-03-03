using DiffEngine;

static partial class Implementation
{
    public static Definition BeyondCompare()
    {
        static string TargetLeftWindowsArguments(string temp, string target)
        {
            return $"/solo /rightreadonly \"{target}\" \"{temp}\"";
        }

        static string TargetRightWindowsArguments(string temp, string target)
        {
            return $"/solo /leftreadonly \"{temp}\" \"{target}\"";
        }

        static string TargetLeftOsxLinuxArguments(string temp, string target)
        {
            return $"-solo -rightreadonly \"{target}\" \"{temp}\"";
        }

        static string TargetRightOsxLinuxArguments(string temp, string target)
        {
            return $"-solo -leftreadonly \"{temp}\" \"{target}\"";
        }

        return new(
            name: DiffTool.BeyondCompare,
            url: "https://www.scootersoftware.com",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            cost: "Paid",
            // technically BC doesnt require a target.
            // but if no target exists, the target cannot be edited
            requiresTarget: true,
            binaryExtensions: new[]
            {
                "mp3", //?
                "xls",
                "xlsm",
                "xlsx",
                "doc",
                "docm",
                "docx",
                "dot",
                "dotm",
                "dotx",
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
            windows: new(
                TargetLeftWindowsArguments,
                TargetRightWindowsArguments,
                @"%ProgramFiles%\Beyond Compare *\BCompare.exe", @"%UserProfile%\scoop\apps\beyondcompare\current\BCompare.exe"),
            linux: new(
                TargetLeftOsxLinuxArguments,
                TargetRightOsxLinuxArguments,
                "/usr/lib/beyondcompare/bcomp"),
            osx: new(
                TargetLeftOsxLinuxArguments,
                TargetRightOsxLinuxArguments,
                "/Applications/Beyond Compare.app/Contents/MacOS/bcomp"),
            notes: @"
 * [Command line reference](https://www.scootersoftware.com/v4help/index.html?command_line_reference.html)");
    }
}