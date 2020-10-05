using DiffEngine;

static partial class Implementation
{
    public static Definition BeyondCompare()
    {
        static string WindowsArguments(string temp, string target) =>
            $"/solo /leftreadonly \"{temp}\" \"{target}\"";

        static string OsxLinuxArguments(string temp, string target) =>
            $"-solo -leftreadonly \"{temp}\" \"{target}\"";

        return new Definition(
            name: DiffTool.BeyondCompare,
            url: "https://www.scootersoftware.com",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
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
            windows: new OsSettings(WindowsArguments, @"%ProgramFiles%\Beyond Compare *\BCompare.exe"),
            linux: new OsSettings(OsxLinuxArguments, "/usr/lib/beyondcompare/bcomp"),
            osx: new OsSettings(OsxLinuxArguments, "/Applications/Beyond Compare.app/Contents/MacOS/bcomp"),
            notes: @"
 * [Command line reference](https://www.scootersoftware.com/v4help/index.html?command_line_reference.html)");
    }
}