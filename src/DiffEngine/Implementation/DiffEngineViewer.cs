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
                    SearchDirectories(@"%USERPROFILE%\.dotnet\tools\", FallbackViewerDirectories.Tray(), FallbackViewerDirectories.Windows())),
                Linux: new(
                    "DiffEngineViewer",
                    launchArguments,
                    SearchDirectories("%HOME%/.dotnet/tools/", [], FallbackViewerDirectories.Linux())),
                Osx: new(
                    "DiffEngineViewer",
                    launchArguments,
                    SearchDirectories("%HOME%/.dotnet/tools/", [], FallbackViewerDirectories.Osx()))),
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
    /// A globally installed tool is preferred, because installing one is an explicit choice of
    /// which viewer to run. Then the tray's copy, then the bundled copy, which is version matched to
    /// the library that is about to launch it, then the NuGet cache's.
    /// </summary>
    static string[] SearchDirectories(string toolsDirectory, IEnumerable<string> tray, IEnumerable<string> nuGet)
    {
        var directories = new List<string>
        {
            toolsDirectory
        };
        directories.AddRange(tray);
        var bundled = BundledViewerDirectory.Find();
        if (bundled != null)
        {
            directories.Add(bundled);
        }

        directories.AddRange(nuGet);
        return directories.ToArray();
    }
}
