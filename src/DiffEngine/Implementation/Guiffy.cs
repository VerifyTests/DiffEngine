using DiffEngine;

static partial class Implementation
{
    public static Definition Guiffy()
    {
        string Arguments(string temp, string target)
        {
            return $"\"{temp}\" \"{target}\" -ge1";
        }

        return new Definition(
            name: DiffTool.Guiffy,
            url: "https://www.guiffy.com/",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            binaryExtensions: new[]
            {
                "bmp", "gif", "jpeg", "jpg", "png", "wbmp"
            },
            windows: new OsSettings(Arguments, @"%ProgramFiles%\Guiffy\guiffy.exe"),
            notes: @"
 * [Command line reference](https://www.guiffy.com/help/GuiffyHelp/GuiffyCmd.html)
 * [Image Diff Tool](https://www.guiffy.com/Image-Diff-Tool.html)
 * `-ge1`: Forbid 1st file view Editing");
    }
}