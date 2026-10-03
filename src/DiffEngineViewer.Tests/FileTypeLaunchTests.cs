extern alias engine;
using System.Globalization;
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
    /// What a run that fails a lot of document snapshots at once leaves the viewer with: one window
    /// holding long PDFs, Office files, maps that take a while to draw, photograph sized pictures
    /// and SVGs, three of each. For watching what happens while they draw.
    /// <para>
    /// Every one of them is drawn off the window's thread, so the window has to answer the whole
    /// time, with a spinner standing in for each picture until it lands. And only the entry on
    /// screen is read and drawn. The first to arrive is on screen, and the rest join the queue
    /// without taking the selection, so each waits to be read and drawn until it is selected.
    /// </para>
    /// </summary>
    [Test]
    [Explicit]
    public async Task ManyDocuments()
    {
        var pairs = new List<(string Name, string Extension, byte[] Received, byte[] Verified)>();
        for (var round = 1; round <= 3; round++)
        {
            pairs.Add(($"Report{round}", ".pdf", LongPdf(round, changed: true), LongPdf(round, changed: false)));
            pairs.Add(($"Letter{round}", ".docx", Edited(".docx", "Hello World!", $"Hello World {round}!"), File.ReadAllBytes(Sample(".docx"))));
            pairs.Add(($"Sheet{round}", ".xlsx", Edited(".xlsx", "Dulce", $"Dulce {round}"), File.ReadAllBytes(Sample(".xlsx"))));
            pairs.Add(($"Slides{round}", ".pptx", Edited(".pptx", "Hello, PowerPoint!", $"Hello, PowerPoint {round}!"), File.ReadAllBytes(Sample(".pptx"))));
            pairs.Add(($"Survey{round}", ".fgb", BusyMap(round, moved: true), BusyMap(round, moved: false)));
            pairs.Add(($"Photo{round}", ".jpg", SampleImages.Photo(220, 120, 60), SampleImages.Photo(60, 120, 220)));
            pairs.Add(($"Logo{round}", ".svg", Encoding.UTF8.GetBytes(SvgOf("red")), Encoding.UTF8.GetBytes(SvgOf("blue"))));
        }

        foreach (var extension in pairs.Select(_ => _.Extension).Distinct())
        {
            await Assert.That(EngineTools.IsDetectedForExtension(EngineTool.DiffEngineViewer, extension)).IsTrue();
        }

        var directory = ManualViewer.TempDirectory();
        var first = pairs[0];
        ManualViewer.Expect(
            "A lot of documents at once",
            $"One window, Pending ({pairs.Count}), with the first to arrive, {first.Name} ({first.Extension.TrimStart('.')}), on screen throughout: the rest join the queue without taking the selection",
            $"Only {first.Name} is read and drawn; the others wait until they are selected",
            "Where a page or picture is still to come, a spinner turns in its place, and the status line says drawing",
            "A long PDF draws its left side first, a page at a time, with the right side's spinner turning until its turn comes, and it moves to the page that differs once both sides have drawn it",
            "The window answers throughout: scroll the text, Tab through the queue, drag the splitter, resize the window",
            "Step to an entry not opened yet: reading text, then spinners, then its pages",
            "Step back to one already drawn: its pages come back without waiting on drawing again",
            "Step quickly past several: only the one stopped on is drawn, once whatever was under way has finished",
            "The 4000 by 3000 photos show a spinner briefly while they are decoded and scaled, and resizing the window rescales them without it stalling");

        var results = new List<EngineLaunch>();
        foreach (var (name, extension, received, verified) in pairs)
        {
            var temp = Path.Combine(directory.FullName, $"{name}.received{extension}");
            var target = Path.Combine(directory.FullName, $"{name}.verified{extension}");
            await File.WriteAllBytesAsync(temp, received);
            await File.WriteAllBytesAsync(target, verified);
            results.Add(await EngineRunner.LaunchAsync(EngineTool.DiffEngineViewer, temp, target));
        }

        // One window: the first pair starts it, and every pair after is handed to it
        await Assert.That(results.Count(_ => _ == EngineLaunch.StartedNewInstance)).IsEqualTo(1);
        await Assert.That(results.Count(_ => _ == EngineLaunch.AlreadyRunningAndSupportsRefresh)).IsEqualTo(pairs.Count - 1);
        await ManualViewer.WaitForClose();
    }

    /// <summary>
    /// A hundred and twenty pages, one of them differing, so a side takes long enough to draw to
    /// watch its pages land and the other side's spinner turn while they do.
    /// </summary>
    internal static byte[] LongPdf(int round, bool changed) =>
        SamplePdf.Build(
            Enumerable.Range(1, 120)
                .Select(_ => changed && _ == round * 30 ? $"Report {round}, page {_}, changed" : $"Report {round}, page {_}")
                .ToArray());

    /// <summary>
    /// Four hundred large squares overlapping, each blended over much of the map: GeoConvert takes a
    /// while to draw them, where a map of that many vertices would be text too long to diff. The last
    /// square is moved on the moved side.
    /// </summary>
    internal static byte[] BusyMap(int round, bool moved)
    {
        const int squares = 400;
        var builder = new StringBuilder("""{"type":"FeatureCollection","features":[""");
        for (var index = 0; index < squares; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            // Two degrees a side, with corners walking round a circle a degree across
            var angle = index * Math.Tau / squares;
            var left = 150 + round + Math.Cos(angle) + (moved && index == squares - 1 ? 0.5 : 0);
            var bottom = -34 + Math.Sin(angle);
            builder.Append(
                CultureInfo.InvariantCulture,
                $$$"""{"type":"Feature","properties":{"index":{{{index}}}},"geometry":{"type":"Polygon","coordinates":[[[{{{left}}},{{{bottom}}}],[{{{left + 2}}},{{{bottom}}}],[{{{left + 2}}},{{{bottom + 2}}}],[{{{left}}},{{{bottom + 2}}}],[{{{left}}},{{{bottom}}}]]]}}""");
        }

        builder.Append("]}");
        var features = GeoConvert.GeoJson.ReadString(builder.ToString());
        using var stream = new MemoryStream();
        GeoConvert.GeoConverter.Write(features, stream, GeoConvert.GeoFormat.FlatGeobuf);
        return stream.ToArray();
    }

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
