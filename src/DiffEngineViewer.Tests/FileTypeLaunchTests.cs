extern alias engine;
using EngineLaunch = engine::DiffEngine.LaunchResult;
using EngineRunner = engine::DiffEngine.DiffRunner;
using EngineTool = engine::DiffEngine.DiffTool;
using EngineTools = engine::DiffEngine.DiffTools;

/// <summary>
/// Launches the viewer built from this working tree on a pair of every file type it compares
/// other than text, through the entry point a test run takes, for a person to look at.
/// <para>
/// What each head draws is the point. Pictures are drawn with each toolkit's own decoder, so which
/// image formats show is per platform, and documents and maps need the documents folder, which a
/// head's bin has and DiffEngine's bundled copy does not. None of that is in a screen snapshot,
/// which stops at the rows every renderer draws.
/// </para>
/// <para>
/// Explicit, as <see cref="ViewerLaunchTests"/> are, and for the same reasons. Run one case, or
/// the class, which opens each in turn as the last is closed:
/// <code>
/// Get-Process DiffEngineViewer -ErrorAction SilentlyContinue | Stop-Process -Force
/// dotnet build src
/// dotnet test --project src/DiffEngineViewer.Tests -- --treenode-filter "/*/*/FileTypeLaunchTests/*"
/// </code>
/// </para>
/// <para>
/// On its own port, and with the tray left out, for the reason <see cref="ViewerLaunchTests.GroupedQueue"/>
/// gives: a running DiffEngineTray would otherwise take the pair into its own queue and show it in
/// its installed viewer rather than this one.
/// </para>
/// </summary>
[NotInParallel]
public class FileTypeLaunchTests
{
    const string port = "3499";

    [Before(Class)]
    public static void Enable()
    {
        ManualViewer.Enable();
        Environment.SetEnvironmentVariable(ViewerClient.PortVariable, port);
        EngineRunner.TrayDisabled = true;
        // Five by default, and the class launches one window per extension. Left raised afterwards:
        // the setter pins a value rather than restoring the ambient one, and nothing else in this
        // assembly launches more than a handful.
        EngineRunner.MaxInstancesToLaunch(100);
    }

    [After(Class)]
    public static void Cleanup()
    {
        ManualViewer.Close();
        Environment.SetEnvironmentVariable(ViewerClient.PortVariable, null);
        EngineRunner.TrayDisabled = false;
    }

    [Test]
    [Explicit]
    [Arguments(".png")]
    [Arguments(".bmp")]
    [Arguments(".gif")]
    [Arguments(".ico")]
    [Arguments(".jpg")]
    [Arguments(".jpeg")]
    [Arguments(".webp")]
    public Task Image(string extension) =>
        Launch(
            extension,
            SampleImages.Build(extension, 220, 40, 40),
            SampleImages.Build(extension, 40, 80, 220),
            $"Image {extension}",
            "Rows for the format, the size and the bytes, coloured where the two differ",
            "Each picture drawn under the rows: red on the left, blue on the right",
            "A format this platform's decoder cannot read shows its rows and no picture, which is expected",
            "The status line says the two are different files");

    [Test]
    [Explicit]
    public Task Pdf() =>
        Launch(
            ".pdf",
            SamplePdf.Build("alpha", "BRAVO", "charlie"),
            SamplePdf.Build("alpha", "bravo", "charlie"),
            "PDF",
            "Text and picture: each page's text above, the page drawn under it",
            "Opens at page 2, the one that differs, and the headers say (page 2 of 3)",
            "The status line says page 2 differs",
            "[ and ] or Prev page and Next page turn both sides together",
            "r, or the view button, cycles to Picture only, then Text only, then back",
            "In Picture only, Prev change and Next change move between the pages that differ");

    [Test]
    [Explicit]
    [Arguments(".docx", "Hello World!")]
    [Arguments(".xlsx", "Dulce")]
    [Arguments(".pptx", "Hello, PowerPoint!")]
    public Task Office(string extension, string text) =>
        Launch(
            extension,
            Edited(extension, text, $"{text} CHANGED"),
            File.ReadAllBytes(Sample(extension)),
            $"Office {extension}",
            "The status line says reading text, then the line range",
            $"The document as Markdown above, one line differing: {text} CHANGED on the left",
            "The page drawn under the text",
            "r cycles the three views, and the page buttons turn pages where there is more than one");

    [Test]
    [Explicit]
    public Task Svg() =>
        Launch(
            ".svg",
            Encoding.UTF8.GetBytes(SvgOf("red")),
            Encoding.UTF8.GetBytes(SvgOf("blue")),
            "SVG",
            "The SVG's source above, the fill line differing",
            "A red circle drawn on the left and a blue one on the right, larger than the 48 pixels the file says",
            "No page buttons, and the status line says the drawings differ",
            "r cycles the three views");

    [Test]
    [Explicit]
    [Arguments(".geojson")]
    [Arguments(".topojson")]
    [Arguments(".kml")]
    [Arguments(".gpx")]
    [Arguments(".wkt")]
    public Task TextMap(string extension) =>
        Launch(
            extension,
            MapOf(extension, moved: true),
            MapOf(extension, moved: false),
            $"Map {extension}",
            "The file itself as the text, the point's coordinates differing",
            "A map drawn under the text: a shaded block, a line across it and a point, the point further east on the left",
            "No page buttons, and the status line says the drawings differ",
            "r cycles the three views");

    [Test]
    [Explicit]
    [Arguments(".kmz")]
    [Arguments(".wkb")]
    [Arguments(".fgb")]
    [Arguments(".geoparquet")]
    public Task BinaryMap(string extension) =>
        Launch(
            extension,
            MapOf(extension, moved: true),
            MapOf(extension, moved: false),
            $"Map {extension}",
            "The status line says reading text, then the line range",
            "The text is GeoJSON, indented, the point's coordinates differing",
            "A map drawn under the text, the point further east on the left",
            "No page buttons, and the status line says the drawings differ");

    /// <summary>
    /// Through <see cref="EngineRunner"/>, which is what a test run calls, after asking DiffEngine
    /// whether it would choose the viewer for this extension at all: a type it would not route
    /// there is never seen in it by anyone but this test.
    /// </summary>
    static async Task Launch(string extension, byte[] received, byte[] verified, string scenario, params string[] checks)
    {
        await Assert.That(EngineTools.IsDetectedForExtension(EngineTool.DiffEngineViewer, extension)).IsTrue();

        var directory = ManualViewer.TempDirectory();
        var temp = Path.Combine(directory.FullName, $"Sample.received{extension}");
        var target = Path.Combine(directory.FullName, $"Sample.verified{extension}");
        await File.WriteAllBytesAsync(temp, received);
        await File.WriteAllBytesAsync(target, verified);

        ManualViewer.Expect(
            scenario,
            [$"Headers Sample.received{extension} and Sample.verified{extension}", .. checks]);

        var result = await EngineRunner.LaunchAsync(EngineTool.DiffEngineViewer, temp, target);

        await Assert.That(result).IsEqualTo(EngineLaunch.StartedNewInstance);
        await ManualViewer.WaitForClose();
    }

    static string Sample(string extension) =>
        Path.Combine(AppContext.BaseDirectory, "DocumentSamples", $"sample{extension}");

    /// <summary>
    /// The committed sample with one string in it changed, wherever in the package that string is
    /// kept: the document body, the shared strings or a slide.
    /// </summary>
    internal static byte[] Edited(string extension, string from, string to)
    {
        using var stream = new MemoryStream();
        stream.Write(File.ReadAllBytes(Sample(extension)));
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            foreach (var entry in archive.Entries.Where(_ => _.FullName.EndsWith(".xml")).ToList())
            {
                string xml;
                using (var reader = new StreamReader(entry.Open()))
                {
                    xml = reader.ReadToEnd();
                }

                if (!xml.Contains(from))
                {
                    continue;
                }

                var name = entry.FullName;
                entry.Delete();
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(xml.Replace(from, to));
            }
        }

        return stream.ToArray();
    }

    internal static string SvgOf(string fill) =>
        $"""
         <svg xmlns="http://www.w3.org/2000/svg" width="48" height="48">
           <circle cx="24" cy="24" r="20" fill="{fill}" />
         </svg>
         """;

    /// <summary>
    /// Sydney Harbour as a block, a bridge and a point, converted by GeoConvert to whichever format
    /// is asked for, so every format holds the same features.
    /// </summary>
    internal static byte[] MapOf(string extension, bool moved)
    {
        var longitude = moved ? "151.235" : "151.215";
        var geoJson =
            $$$"""
               {"type":"FeatureCollection","features":[
                 {"type":"Feature","properties":{"name":"Harbour"},"geometry":{"type":"Polygon","coordinates":[[[151.2,-33.86],[151.24,-33.86],[151.24,-33.84],[151.2,-33.84],[151.2,-33.86]]]}},
                 {"type":"Feature","properties":{"name":"Bridge"},"geometry":{"type":"LineString","coordinates":[[151.21,-33.85],[151.23,-33.85]]}},
                 {"type":"Feature","properties":{"name":"Opera House"},"geometry":{"type":"Point","coordinates":[{{{longitude}}},-33.857]}}
               ]}
               """;
        var features = GeoConvert.GeoJson.ReadString(geoJson);
        var format = GeoConvert.GeoConverter.DetectFormat($"map{extension}");
        using var stream = new MemoryStream();
        GeoConvert.GeoConverter.Write(features, stream, format);
        return stream.ToArray();
    }
}
