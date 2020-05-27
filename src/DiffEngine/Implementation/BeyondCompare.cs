using DiffEngine;

static partial class Implementation
{
    public static Definition BeyondCompare()
    {
        string WindowsArguments(string temp, string target) =>
            $"/solo /leftreadonly \"{temp}\" \"{target}\"";
        string OsxLinuxArguments(string temp, string target) =>
            $"-solo -leftreadonly \"{temp}\" \"{target}\"";

        return new Definition(
            name: DiffTool.BeyondCompare,
            url: "https://www.scootersoftware.com",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: false,
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