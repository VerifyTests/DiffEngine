using DiffEngine;

static partial class Implementation
{
    public static ToolDefinition BeyondCompare()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"/solo \"{tempFile}\" \"{targetFile}\"";

        return new ToolDefinition(
            name: DiffTool.BeyondCompare,
            url: "https://www.scootersoftware.com",
            supportsAutoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: false,
            windowsArguments: BuildArguments,
            linuxArguments: BuildArguments,
            osxArguments: BuildArguments,
            windowsExePaths: new[]
            {
                @"%ProgramFiles%\Beyond Compare *\BCompare.exe"
            },
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
            linuxExePaths: new[]
            {
                //TODO:
                "/usr/lib/beyondcompare/bcomp"
            },
            osxExePaths: new[]
            {
                "/Applications/Beyond Compare.app/Contents/MacOS/bcomp"
            },
            notes: @"
 * [Command line reference](https://www.scootersoftware.com/v4help/index.html?command_line_reference.html)");
    }
}