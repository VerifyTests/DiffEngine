using DiffEngine;

static partial class Implementation
{
    public static Definition BeyondCompare()
    {
        string Arguments(string temp, string target) =>
            $"/solo \"{temp}\" \"{target}\"";

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
            windows: new OsSettings(Arguments, @"%ProgramFiles%\Beyond Compare *\BCompare.exe"),
            linux: new OsSettings(Arguments, "/usr/lib/beyondcompare/bcomp"),
            osx: new OsSettings(Arguments, "/Applications/Beyond Compare.app/Contents/MacOS/bcomp"),
            notes: @"
 * [Command line reference](https://www.scootersoftware.com/v4help/index.html?command_line_reference.html)");
    }
}