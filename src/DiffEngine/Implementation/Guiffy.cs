using DiffEngine;

static partial class Implementation
{
    public static Definition Guiffy()
    {
        static string Arguments(string temp, string target)
        {
            return $"\"{temp}\" \"{target}\" -ge1";
        }

        return new(
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
            windows: new(Arguments, @"%ProgramFiles%\Guiffy\guiffy.exe"),
            osx: new(Arguments, "/Applications/Guiffy/guiffyCL.command"),
            notes: @"
 * [Command line reference](https://www.guiffy.com/help/GuiffyHelp/GuiffyCmd.html)
 * [Image Diff Tool](https://www.guiffy.com/Image-Diff-Tool.html)
 * `-ge1`: Forbid 1st file view Editing");
    }
}