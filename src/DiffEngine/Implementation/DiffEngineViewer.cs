static partial class Implementation
{
    public static Definition DiffEngineViewer()
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"\"{target}\" \"{temp}\"",
            Right: (temp, target) => $"\"{temp}\" \"{target}\"");

        return new(
            Tool: DiffTool.DiffEngineViewer,
            Url: "https://github.com/VerifyTests/DiffEngine",
            AutoRefresh: false,
            IsMdi: false,
            SupportsText: true,
            RequiresTarget: true,
            BinaryExtensions: ImageExtensions.All,
            Cost: "Free",
            OsSupport: new(
                Windows: new(
                    "DiffEngineViewer.exe",
                    launchArguments,
                    SearchDirectories(@"%USERPROFILE%\.dotnet\tools\")),
                Linux: new(
                    "DiffEngineViewer",
                    launchArguments,
                    SearchDirectories("%HOME%/.dotnet/tools/")),
                Osx: new(
                    "DiffEngineViewer",
                    launchArguments,
                    SearchDirectories("%HOME%/.dotnet/tools/"))),
            UseShellExecute: false,
            // Console subsystem, so without this a window flashes on every launch.
            CreateNoWindow: true,
            Notes: """
                 * The one tool DiffEngine does not open per pair. Every failing pair joins one
                   window, so the auto-refresh and MDI table above does not describe it: nothing
                   is relaunched, nothing is killed, and a test that starts passing has its entry
                   dropped instead
                 * Bundled inside the DiffEngine package, so it needs no install
                 * Also available standalone as `DiffEngineViewer.Windows`, `.Mac` or `.Linux`
                 * Renders natively per platform: WinForms on Windows, AppKit and Core Text on
                   macOS, Dear ImGui through raylib on Linux
                 * Compares images by format, dimensions and content, and draws them, with
                   whichever formats each platform's own decoder reads
                """);
    }

    /// <summary>
    /// The bundled copy is preferred over a globally installed tool, because it is always version
    /// matched to the library that is about to launch it.
    /// </summary>
    static string[] SearchDirectories(string toolsDirectory)
    {
        var bundled = BundledViewerDirectory.Find();
        if (bundled == null)
        {
            return [toolsDirectory];
        }

        return
        [
            bundled,
            toolsDirectory
        ];
    }
}
