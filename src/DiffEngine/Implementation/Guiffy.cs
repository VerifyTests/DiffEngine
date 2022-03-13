static partial class Implementation
{
    public static Definition Guiffy()
    {
        static string TargetLeftArguments(string temp, string target)
        {
            return $"\"{target}\" \"{temp}\" -ge2";
        }

        static string TargetRightArguments(string temp, string target)
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
                TargetLeftArguments,
                TargetRightArguments,
                @"%ProgramFiles%\Guiffy\guiffy.exe"),
            osx: new(
                TargetLeftArguments,
                TargetRightArguments,
                "/Applications/Guiffy/guiffyCL.command"),
            notes: @"
 * [Command line reference](https://www.guiffy.com/help/GuiffyHelp/GuiffyCmd.html)
 * [Image Diff Tool](https://www.guiffy.com/Image-Diff-Tool.html)
 * `-ge1`: Forbid first file view Editing
 * `-ge2`: Forbid second file view Editing");
    }
}