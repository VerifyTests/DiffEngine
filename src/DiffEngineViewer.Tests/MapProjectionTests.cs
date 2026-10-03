/// <summary>
/// Switching the projection maps are drawn in: a setting of the window, the same on both sides,
/// kept for the next map and for the next run.
/// <para>
/// Sides and pages are numbers and fake paths wherever a screen is all that is built, as in
/// <see cref="DocumentScreenTests"/>. The watch runs over real files and a renderer that draws a
/// "map" as one png whose colour is the projection it was asked for.
/// </para>
/// </summary>
public class MapProjectionTests :
    IDisposable
{
    [Test]
    public async Task TheProjectionCycles()
    {
        var state = Map();
        var seen = new List<MapProjection>();
        for (var step = 0; step < 5; step++)
        {
            state = ViewerSession.Apply(state, CommandKind.NextProjection);
            seen.Add(state.Projection);
        }

        // Joined, so the assertion is about the order as well as the members
        await Assert.That(string.Join(", ", seen)).IsEqualTo("PlateCarree, WebMercator, Lambert, Goode, Auto");
    }

    /// <summary>
    /// A key skips the button's enabled check, so the command itself refuses whatever is not a map,
    /// and a map with no picture on screen for a projection to change.
    /// </summary>
    [Test]
    public async Task OnlyAMapBeingDrawnHasAProjectionToSwitch()
    {
        var text = Fixtures.File();
        await Assert.That(ViewerSession.Apply(text, CommandKind.NextProjection)).IsSameReferenceAs(text);

        var pdf = DocumentScreenTests.State(DocumentScreenTests.Left, DocumentScreenTests.Right);
        await Assert.That(ViewerSession.Apply(pdf, CommandKind.NextProjection)).IsSameReferenceAs(pdf);

        var svg = Document(DocumentFormat.Svg, ".svg");
        await Assert.That(ViewerSession.Apply(svg, CommandKind.NextProjection)).IsSameReferenceAs(svg);

        var textOnly = Map().Showing(DrawingView.Text);
        await Assert.That(ViewerSession.Apply(textOnly, CommandKind.NextProjection)).IsSameReferenceAs(textOnly);
    }

    /// <summary>
    /// The same bytes draw differently in each projection, so each is kept under its own key. The
    /// one switched to has nothing yet, which is what has the watch draw it and a head show its
    /// spinner; the one switched back to is still there.
    /// </summary>
    [Test]
    public async Task EachProjectionKeepsItsOwnPages()
    {
        var state = Drawn(Map());
        await Assert.That(ScreenBuilder.Build(state).Left.Image!.Path).IsEqualTo("render/auto-left.png");

        var switched = ViewerSession.Apply(state, CommandKind.NextProjection);
        var waiting = ScreenBuilder.Build(switched);
        await Assert.That(waiting.Left.Image).IsNull();
        await Assert.That(waiting.Left.ImagePending).IsTrue();

        switched = ViewerSession.Rendered(
            switched,
            DocumentPages.Key(Left, MapProjection.PlateCarree)!,
            new([Page("flat-left")], true));
        await Assert.That(ScreenBuilder.Build(switched).Left.Image!.Path).IsEqualTo("render/flat-left.png");

        var back = switched with { Projection = MapProjection.Auto };
        await Assert.That(ScreenBuilder.Build(back).Left.Image!.Path).IsEqualTo("render/auto-left.png");
    }

    /// <summary>
    /// Unswitched, a map is kept under its bare hash, where every document always was, and a
    /// document that is not a map is kept there whatever the projection.
    /// </summary>
    [Test]
    public async Task OnlyAMapInAChosenProjectionHasAKeyOfItsOwn()
    {
        await Assert.That(DocumentPages.Key(Left, MapProjection.Auto)).IsEqualTo("M1");
        await Assert.That(DocumentPages.Key(Left, MapProjection.Goode)).IsEqualTo("M1.Goode");
        await Assert.That(DocumentPages.Key(DocumentScreenTests.Left, MapProjection.Goode)).IsEqualTo("AA");
        await Assert.That(DocumentPages.Key(Left with { Hash = null }, MapProjection.Goode)).IsNull();
        await Assert.That(DocumentPages.HashOf("M1.Goode")).IsEqualTo("M1");
        await Assert.That(DocumentPages.HashOf("M1")).IsEqualTo("M1");
    }

    /// <summary>
    /// What a map drew as in every projection stays for as long as the map is queued, and goes
    /// with it.
    /// </summary>
    [Test]
    public async Task EveryProjectionsPagesGoWithTheMap()
    {
        var state = Drawn(Map());
        state = ViewerSession.Rendered(state, "M1.Goode", new([Page("goode-left")], true));
        await Assert.That(state.Renders.Count).IsEqualTo(3);
        await Assert.That(ViewerSession.Forget(state)).IsSameReferenceAs(state);

        var emptied = state with
        {
            Queue = [QueueEntry.ForFiles("a.txt", "b.txt", FileSide.OfText("a"), FileSide.OfText("b"))]
        };
        await Assert.That(ViewerSession.Forget(emptied).Renders).IsEmpty();
    }

    /// <summary>
    /// The button says which projection is on screen, which is the only place a renderer that
    /// draws no picture can say it.
    /// </summary>
    [Test]
    public async Task TheButtonNamesTheProjectionOnScreen()
    {
        var state = Map();
        await Assert.That(Projection(state)).IsEqualTo(new("Projection: Auto", true, CommandKind.NextProjection));

        state = ViewerSession.Apply(state, CommandKind.NextProjection);
        await Assert.That(Projection(state)!.Label).IsEqualTo("Projection: Equirectangular");

        // Still there in the text view, so the buttons after it do not move, and no use
        var textOnly = Projection(state.Showing(DrawingView.Text));
        await Assert.That(textOnly).IsEqualTo(new("Projection: Equirectangular", false, CommandKind.NextProjection));
    }

    [Test]
    public async Task OnlyAMapHasTheButton()
    {
        await Assert.That(Projection(Fixtures.File())).IsNull();
        await Assert.That(Projection(DocumentScreenTests.State(DocumentScreenTests.Left, DocumentScreenTests.Right))).IsNull();
        await Assert.That(Projection(Document(DocumentFormat.Svg, ".svg"))).IsNull();
        await Assert.That(Projection(Document(DocumentFormat.FlatGeobuf, ".fgb"))).IsNotNull();
    }

    [Test]
    public Task AMapInAChosenProjection()
    {
        var state = Map() with { Projection = MapProjection.WebMercator };
        state = ViewerSession.Rendered(state, "M1.WebMercator", new([Page("mercator-left")], true));
        state = ViewerSession.Rendered(state, "M2.WebMercator", new([Page("mercator-right")], true));
        return Verify(Fixtures.Render(state));
    }

    /// <summary>
    /// Switching draws both sides again, in the projection switched to, into a folder of its own:
    /// a page is a path to the heads, and the same path rewritten would be the picture they
    /// already hold.
    /// </summary>
    [Test]
    public async Task TheWatchDrawsTheMapAgainInTheProjectionSwitchedTo()
    {
        var (host, documents) = Owned();
        var watch = new DocumentWatch(host, documents.Plugin);
        Drain(watch);
        await Assert.That(string.Join(", ", documents.Projections)).IsEqualTo("Auto, Auto");
        var first = ScreenBuilder.Build(host.State).Left.Image!;

        host.Mutate(_ => ViewerSession.Apply(_, CommandKind.NextProjection));
        Drain(watch);

        await Assert.That(string.Join(", ", documents.Projections)).IsEqualTo("Auto, Auto, PlateCarree, PlateCarree");
        var second = ScreenBuilder.Build(host.State).Left.Image!;
        await Assert.That(second.Path).IsNotEqualTo(first.Path);
        await Assert.That(Path.GetFileName(Path.GetDirectoryName(second.Path))).IsEqualTo("PlateCarree");
        await Assert.That(File.Exists(first.Path)).IsTrue();
        await Assert.That(second.Hash)!.IsNotEqualTo(first.Hash);
    }

    [Test]
    public async Task GoingBackToAProjectionAlreadyDrawnDrawsNothing()
    {
        var (host, documents) = Owned();
        var watch = new DocumentWatch(host, documents.Plugin);
        Drain(watch);
        host.Mutate(_ => ViewerSession.Apply(_, CommandKind.NextProjection));
        Drain(watch);

        host.Mutate(_ => _ with { Projection = MapProjection.Auto });

        await Assert.That(watch.Pump()).IsFalse();
        await Assert.That(documents.Projections.Count).IsEqualTo(4);
    }

    /// <summary>
    /// A PDF is the same pages whatever maps are drawn in, so a projection chosen for a map
    /// earlier in the queue costs it nothing.
    /// </summary>
    [Test]
    public async Task ADocumentThatIsNotAMapIsNotDrawnAgain()
    {
        var (host, documents) = Owned(".pdf");
        var watch = new DocumentWatch(host, documents.Plugin);
        Drain(watch);
        var drawn = documents.Projections.Count;

        host.Mutate(_ => _ with { Projection = MapProjection.Goode });

        await Assert.That(watch.Pump()).IsFalse();
        await Assert.That(documents.Projections.Count).IsEqualTo(drawn);
    }

    [Test]
    public async Task TheProjectionIsRemembered()
    {
        var path = Path.Combine(directory, "viewer.settings");

        new ViewerPreferences(path).Remember(Map() with { Projection = MapProjection.Goode });

        var next = new ViewerPreferences(path);
        await Assert.That(next.Projection).IsEqualTo(MapProjection.Goode);
        await Assert.That(next.Apply(Map()).Projection).IsEqualTo(MapProjection.Goode);
    }

    /// <summary>
    /// Auto is what there is with nothing remembered, so going back to it is forgetting.
    /// </summary>
    [Test]
    public async Task AutoIsNotWrittenDown()
    {
        var path = Path.Combine(directory, "viewer.settings");
        var preferences = new ViewerPreferences(path);
        preferences.Remember(Map() with { Projection = MapProjection.Goode });

        preferences.Remember(Map());

        await Assert.That(new ViewerPreferences(path).Get("projection")).IsNull();
    }

    [Test]
    [Arguments("projection=Sideways")]
    [Arguments("projection=3")]
    [Arguments("projection=")]
    public async Task AProjectionThatIsNotOneIsAuto(string line)
    {
        var path = Path.Combine(directory, "viewer.settings");
        await File.WriteAllTextAsync(path, line);

        await Assert.That(new ViewerPreferences(path).Projection).IsEqualTo(MapProjection.Auto);
    }

    /// <summary>
    /// The loop itself: a window opens in the projection the last one was left in, and one the
    /// reader switches to is kept as they switch, not on the way out.
    /// </summary>
    [Test]
    public async Task TheLoopOpensInTheRememberedProjectionAndKeepsTheNext()
    {
        var preferences = new ViewerPreferences
        {
            Projection = MapProjection.Lambert
        };
        var host = new SessionHost(Map());
        var window = new KeyWindow(CommandKind.NextProjection);

        IViewerWindow Open(string title, int width, int height, bool hidden, WindowPlacement? placement, out string? error)
        {
            error = null;
            return window;
        }

        ViewerProgram.Run(host, server: null, link: null, Open, preferences: preferences);

        await Assert.That(window.First!.Buttons.Select(_ => _.Label)).Contains("Projection: Lambert conic");
        await Assert.That(host.State.Projection).IsEqualTo(MapProjection.Goode);
        await Assert.That(preferences.Projection).IsEqualTo(MapProjection.Goode);
    }

    static Button? Projection(SessionState state) =>
        ScreenBuilder.Build(state).Buttons.SingleOrDefault(_ => _.Command == CommandKind.NextProjection);

    static DocumentFile Left { get; } = new("temp/map.received.geojson", 300, DocumentFormat.GeoJson, "M1");
    static DocumentFile Right { get; } = new("code/map.verified.geojson", 310, DocumentFormat.GeoJson, "M2");

    static RenderedPage Page(string name) =>
        new($"render/{name}.png", 2048, 1024, name);

    static SessionState Drawn(SessionState state)
    {
        state = ViewerSession.Rendered(state, "M1", new([Page("auto-left")], true));
        return ViewerSession.Rendered(state, "M2", new([Page("auto-right")], true));
    }

    /// <summary>
    /// Wide, as <see cref="DocumentScreenTests"/> is: the projection button is one more in the
    /// footer, and a footer that runs out of room cuts the status line short.
    /// </summary>
    const int columns = 200;

    static SessionState Map() =>
        ViewerSession.EnqueueFile(
            SessionState.Start(ViewerMode.File, columns, Fixtures.Rows),
            QueueEntry.ForFiles(
                Left.Path,
                Right.Path,
                new(GeoJson("151.21"), null, null, null, Left),
                new(GeoJson("151.23"), null, null, null, Right)));

    static SessionState Document(DocumentFormat format, string extension) =>
        ViewerSession.EnqueueFile(
            SessionState.Start(ViewerMode.File, columns, Fixtures.Rows),
            QueueEntry.ForFiles(
                $"temp/sample.received{extension}",
                $"code/sample.verified{extension}",
                new("left", null, null, null, new($"temp/sample.received{extension}", 120, format, "D1")),
                new("right", null, null, null, new($"code/sample.verified{extension}", 121, format, "D2"))));

    static string GeoJson(string longitude) =>
        $$"""
          {
            "type": "Feature",
            "geometry": {
              "type": "Point",
              "coordinates": [{{longitude}}, -33.85]
            }
          }
          """;

    static void Drain(DocumentWatch watch)
    {
        while (watch.Pump())
        {
        }
    }

    (SessionHost Host, FakeMaps Documents) Owned(string extension = ".geojson")
    {
        var documents = new FakeMaps();
        disposables.Add(documents);
        var left = Write($"sample.received{extension}", GeoJson("151.21"));
        var right = Write($"sample.verified{extension}", GeoJson("151.23"));
        var entry = QueueEntry.ForFiles(
            left,
            right,
            FileSide.Read(left, documents.Plugin),
            FileSide.Read(right, documents.Plugin));
        var state = ViewerSession.EnqueueFile(SessionState.Start(ViewerMode.File, columns, Fixtures.Rows), entry);
        return (new(state), documents);
    }

    string Write(string name, string content)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    readonly string directory = Directory.CreateTempSubdirectory("deview-map-projection-").FullName;
    readonly List<IDisposable> disposables = [];

    public void Dispose()
    {
        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }

        Directory.Delete(directory, true);
    }

    /// <summary>
    /// Draws anything as one png, coloured by the file and by the projection asked for, and
    /// remembers what it was asked.
    /// </summary>
    sealed class FakeMaps :
        IDisposable
    {
        public FakeMaps() =>
            Plugin = new(File.ReadAllText, Render);

        public DocumentPlugin Plugin { get; }

        public List<string> Projections { get; } = [];

        int Render(string path, string directory, string projection, Action<string> landed)
        {
            // Both sides are drawn at once, each on a thread of its own
            lock (Projections)
            {
                Projections.Add(projection);
            }

            var colour = SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(path) + projection));
            var page = Path.Combine(directory, "page_0001.png");
            File.WriteAllBytes(page, SamplePng.Build(8, 8, colour[0], colour[1], colour[2]));
            landed(page);
            return 1;
        }

        public void Dispose() =>
            Plugin.Dispose();
    }

    /// <summary>
    /// A window that presses one key on its first frame and has closed by its second.
    /// </summary>
    sealed class KeyWindow(CommandKind key) : IViewerWindow
    {
        int frames;

        public Screen? First { get; private set; }

        public bool Present(Screen screen)
        {
            First ??= screen;
            return frames++ == 0;
        }

        public ViewerInput Poll() =>
            new(key, -1, -1, 0, false, columns, Fixtures.Rows);

        public void SetHidden(bool hidden)
        {
        }

        public void Focus()
        {
        }

        public void SetClipboard(string text)
        {
        }

        public bool Capture(Screen screen, int width, int height, string pngPath) =>
            false;

        public void Dispose()
        {
        }
    }
}
