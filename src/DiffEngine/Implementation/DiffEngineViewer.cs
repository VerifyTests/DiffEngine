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
            // A new snapshot has no verified file, and the viewer says so: it reads a missing
            // target as an empty side. Asking for one meant EmptyFiles wrote a placeholder first,
            // which the viewer then compared against as though it were the expected file - an
            // empty PDF it could not open, a blank page - and for the maps, which EmptyFiles has
            // no file for, that the pair never reached the viewer at all.
            RequiresTarget: false,
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
                 * The standalone tool and the copy installed with DiffEngineTray also read PDF,
                   docx, xlsx and pptx files - as text, as pages drawn, or both - and draw SVGs
                   and maps (GeoJSON, TopoJSON, KML, KMZ, GPX, WKT, WKB, FlatGeobuf and
                   GeoParquet) beside their text. Offered for those files only when the copy found
                   is one of these, never the bundled one, which stays small
                """);
    }

    /// <summary>
    /// A globally installed tool is preferred, because installing one is an explicit choice of
    /// which viewer to run. Then the tray's copy, then the bundled copy, which is version matched to
    /// the library that is about to launch it, then the NuGet cache's.
    /// <para>
    /// That is the order copies are looked for in, and not quite the order they are taken in: one
    /// from before the way this library starts a viewer is passed over while a newer one is
    /// further down. See <see cref="DiffEngine.ViewerContract" />.
    /// </para>
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
