static partial class Implementation
{
    public static Definition Guiffy()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"\"{target}\" \"{temp}\" -ge2",
            Right: (temp, target) => $"\"{temp}\" \"{target}\" -ge1");

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
                launchArguments,
                @"%ProgramFiles%\Guiffy\"),
            osx: new(
                "guiffyCL.command",
                launchArguments,
                "/Applications/Guiffy/"),
            notes: @"
 * [Command line reference](https://www.guiffy.com/help/GuiffyHelp/GuiffyCmd.html)
 * [Image Diff Tool](https://www.guiffy.com/Image-Diff-Tool.html)
 * `-ge1`: Forbid first file view Editing
 * `-ge2`: Forbid second file view Editing");
    }
}