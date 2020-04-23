using DiffEngine;

static partial class Implementation
{
    public static Definition BeyondCompare()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"/solo \"{tempFile}\" \"{targetFile}\"";

        return new Definition(
            name: DiffTool.BeyondCompare,
            url: "https://www.scootersoftware.com",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: false,
            windows: new OsSettings(
                BuildArguments,
                new[]
                {
                    @"%ProgramFiles%\Beyond Compare *\BCompare.exe"
                }),
            linux: new OsSettings(
                BuildArguments,
                new[]
                {
                    //TODO:
                    "/usr/lib/beyondcompare/bcomp"
                }),
            osx: new OsSettings(
                BuildArguments,
                new[]
                {
                    "/Applications/Beyond Compare.app/Contents/MacOS/bcomp"
                }),
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
            notes: @"
 * [Command line reference](https://www.scootersoftware.com/v4help/index.html?command_line_reference.html)");
    }
}