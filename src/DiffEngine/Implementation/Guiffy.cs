static partial class Implementation
{
    public static Definition Guiffy()
    {
        static string LeftArguments(string temp, string target)
        {
            return $"\"{target}\" \"{temp}\" -ge2";
        }

        static string RightArguments(string temp, string target)
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
            cost: "Paid",
            binaryExtensions: new[]
            {
                "bmp", "gif", "jpeg", "jpg", "png", "wbmp"
            },
            windows: new(
                "guiffy.exe",
                LeftArguments,
                RightArguments,
                @"%ProgramFiles%\Guiffy\"),
            osx: new(
                "guiffyCL.command",
                LeftArguments,
                RightArguments,
                "/Applications/Guiffy/"),
            notes: @"
 * [Command line reference](https://www.guiffy.com/help/GuiffyHelp/GuiffyCmd.html)
 * [Image Diff Tool](https://www.guiffy.com/Image-Diff-Tool.html)
 * `-ge1`: Forbid first file view Editing
 * `-ge2`: Forbid second file view Editing");
    }
}