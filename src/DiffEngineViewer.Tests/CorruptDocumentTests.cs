/// <summary>
/// A file that is not the document its extension says, for every kind the viewer reads: empty,
/// something else entirely, or a real one cut short, which is what a test that failed part way
/// through writing its snapshot leaves behind.
/// <para>
/// Through the real documents folder, since what is being pinned is what Morph, PDFium, Svg.Skia
/// and GeoConvert each do with bytes that are not theirs: that none of them hangs or takes the
/// process with it, and that what the reviewer is told is about the file rather than about a
/// library's insides.
/// </para>
/// </summary>
public class CorruptDocumentTests :
    IDisposable
{
    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task ADamagedFileSaysWhatItIsNot(string extension, string damage)
    {
        var path = Write($"damaged{extension}", Damage(Valid(extension), damage));

        var drawn = Failure(() => Render(path));
        await Assert.That(drawn).IsEqualTo(Expected(extension, damage, drawn));

        // The formats whose text is the file are never read here: they are read as any text is
        if (ReadsText(extension))
        {
            var read = Failure(() => Plugin.Text(path));
            await Assert.That(read).IsEqualTo(Expected(extension, damage, read));
        }
    }

    /// <summary>
    /// What the libraries say on their own, which is the reason this is put into words: a reviewer
    /// told the end of a central directory record could not be found has been told nothing.
    /// </summary>
    [Test]
    [Arguments(".docx", "Word document")]
    [Arguments(".xlsx", "Excel workbook")]
    [Arguments(".pptx", "PowerPoint presentation")]
    [Arguments(".kmz", "KMZ map")]
    public async Task AnArchiveThatIsNotOneSaysSo(string extension, string kind)
    {
        var path = Write($"text{extension}", "an error page, saved under the wrong name"u8.ToArray());

        await Assert.That(Failure(() => Render(path)))
            .IsEqualTo($"Not a readable {kind}: it is not a zip archive, or was cut short.");
    }

    /// <summary>
    /// An archive that opens and holds the wrong thing gets as far as the reader, and what that
    /// says is passed on under the same lead-in.
    /// </summary>
    [Test]
    public async Task AnArchiveOfSomethingElseIsNotAWordDocument()
    {
        var path = Path.Combine(directory, "other.docx");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("readme.txt").Open());
            writer.Write("not a document");
        }

        await Assert.That(Failure(() => Plugin.Text(path))).StartsWith("Not a readable Word document: ");
        await Assert.That(Failure(() => Render(path))).StartsWith("Not a readable Word document: ");
    }

    /// <summary>
    /// The lead-ins the libraries put in front of their own reasons repeat what has just been said,
    /// so they are dropped: not "Not a readable KML map: Invalid KML data: Root element...".
    /// </summary>
    [Test]
    public async Task TheLibrarysOwnLeadInIsNotRepeated()
    {
        var kml = Write("other.kml", "<kml"u8.ToArray());
        await Assert.That(Failure(() => Render(kml))).DoesNotContain("Invalid KML data");

        var pdf = Write("other.pdf", "not a pdf"u8.ToArray());
        await Assert.That(Failure(() => Render(pdf))).IsEqualTo("Not a readable PDF: file is not a PDF or is corrupt");
    }

    /// <summary>
    /// A reason that is already about the file is passed on as it is, rather than being told it is
    /// not readable as well.
    /// </summary>
    [Test]
    public async Task AMapWithNothingInItIsNotCalledUnreadable()
    {
        var path = Write("empty.geojson", """{"type":"FeatureCollection","features":[]}"""u8.ToArray());

        await Assert.That(Failure(() => Render(path))).IsEqualTo("The map has no features to draw.");
    }

    /// <summary>
    /// A damaged file costs nothing beyond itself. PDFium is one lock for the process, and a load
    /// that failed while holding it would fail every PDF after.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(Extensions))]
    public async Task TheNextDocumentStillDraws(string extension)
    {
        var damaged = Write($"damaged{extension}", Damage(Valid(extension), "cut short"));
        Failure(() => Render(damaged));

        var fine = Write($"fine{extension}", Valid(extension));

        await Assert.That(Render(fine)).IsGreaterThan(0);
    }

    /// <summary>
    /// The whole of it as a viewer runs it: a received file cut short beside a verified one that
    /// is whole. The reason is said once, about the file; the side that is fine is still read and
    /// drawn; and the window carries on.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(Extensions))]
    public async Task AViewerSaysSoOnceAndDrawsTheOtherSide(string extension)
    {
        using var documents = DocumentPlugin.Find(Path.Combine(AppContext.BaseDirectory, "documents"))!;
        var left = Write($"sample.received{extension}", Damage(Valid(extension), "cut short"));
        var right = Write($"sample.verified{extension}", Valid(extension));
        var host = new SessionHost(ViewerSession.EnqueueFile(
            SessionState.Start(ViewerMode.File, 240, Fixtures.Rows),
            QueueEntry.ForFiles(left, right, FileSide.Read(left, documents), FileSide.Read(right, documents))));

        var watch = new DocumentWatch(host, documents);
        while (watch.Pump())
        {
        }

        var state = host.State;
        var screen = ScreenBuilder.Build(state);
        await Assert.That(screen.Left.Image).IsNull();
        await Assert.That(screen.Left.ImagePending).IsFalse();
        await Assert.That(screen.Right.Image).IsNotNull();
        await Assert.That(screen.Left.Header).IsEqualTo($"sample.received{extension} (not drawn)");

        var reason = DocumentPages.Of(state, state.Current!.LeftDocument)!.Failure!.TrimEnd('.');
        var said = ReadsText(extension)
            // Unreadable and undrawable for the one reason, which is said once
            ? $"could not read sample.received{extension}: {reason}"
            : $"could not draw sample.received{extension}: {reason}";
        await Assert.That(screen.Status).EndsWith(said);
        await Assert.That(Occurrences(screen.Status, "Not a readable")).IsEqualTo(1);
    }

    public static IEnumerable<(string, string)> Cases()
    {
        foreach (var extension in extensions)
        {
            foreach (var damage in (string[]) ["empty", "other text", "other bytes", "cut short"])
            {
                yield return (extension, damage);
            }
        }
    }

    public static IEnumerable<string> Extensions() =>
        extensions;

    static readonly string[] extensions =
    [
        ".pdf", ".docx", ".xlsx", ".pptx", ".svg",
        ".geojson", ".topojson", ".kml", ".gpx", ".wkt",
        ".kmz", ".wkb", ".fgb", ".geoparquet"
    ];

    /// <summary>
    /// One line, saying the file is empty or which kind of document it is not. Whatever follows
    /// the lead-in is the library's and is not pinned, beyond there being something.
    /// </summary>
    static string Expected(string extension, string damage, string actual)
    {
        if (damage == "empty")
        {
            return "The file is empty.";
        }

        var lead = $"Not a readable {Kind(extension)}: ";
        if (actual.StartsWith(lead) &&
            actual.Length > lead.Length &&
            !actual.Contains('\n'))
        {
            return actual;
        }

        return $"{lead}<a reason, on one line>";
    }

    static string Kind(string extension) =>
        extension switch
        {
            ".pdf" => "PDF",
            ".docx" => "Word document",
            ".xlsx" => "Excel workbook",
            ".pptx" => "PowerPoint presentation",
            ".svg" => "SVG",
            ".geojson" => "GeoJSON map",
            ".topojson" => "TopoJSON map",
            ".kml" => "KML map",
            ".kmz" => "KMZ map",
            ".gpx" => "GPX map",
            ".wkt" => "WKT map",
            ".wkb" => "WKB map",
            ".fgb" => "FlatGeobuf map",
            _ => "GeoParquet map"
        };

    static bool ReadsText(string extension) =>
        extension is ".pdf" or ".docx" or ".xlsx" or ".pptx" or ".kmz" or ".wkb" or ".fgb" or ".geoparquet";

    static byte[] Damage(byte[] valid, string damage) =>
        damage switch
        {
            "empty" => [],
            "other text" => "not what the extension says at all"u8.ToArray(),
            "other bytes" => Enumerable.Range(0, 4096).Select(_ => (byte) (_ * 31 + 7)).ToArray(),
            _ => valid[..(valid.Length / 2)]
        };

    static byte[] Valid(string extension) =>
        extension switch
        {
            ".pdf" => SamplePdf.Build("alpha", "bravo", "charlie"),
            ".docx" or ".xlsx" or ".pptx" => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "DocumentSamples", $"sample{extension}")),
            ".svg" => Encoding.UTF8.GetBytes(FileTypeLaunchTests.SvgOf("red")),
            _ => FileTypeLaunchTests.MapOf(extension, moved: false)
        };

    /// <summary>
    /// What a call that has to fail said, having failed inside the time a hang would not.
    /// </summary>
    static string Failure(Action action)
    {
        var task = Task.Run(action);
        try
        {
            if (!task.Wait(TimeSpan.FromSeconds(30)))
            {
                return "<still running after thirty seconds>";
            }
        }
        catch (AggregateException exception)
        {
            var inner = exception.InnerException!;
            // Said as the type it is thrown as, which is what DocumentWatch reports the message of
            return inner is InvalidDataException ? inner.Message : $"<{inner.GetType().Name}> {inner.Message}";
        }

        return "<read without complaint>";
    }

    static int Occurrences(string text, string value) =>
        text.Split(value).Length - 1;

    static DocumentPlugin Plugin { get; } = DocumentPlugin.Find(Path.Combine(AppContext.BaseDirectory, "documents"))!;

    int Render(string path)
    {
        var pages = Directory.CreateDirectory(Path.Combine(directory, $"{Path.GetFileName(path)}-pages")).FullName;
        return Plugin.Render(path, pages, _ => { });
    }

    string Write(string name, byte[] content)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    readonly string directory = Directory.CreateTempSubdirectory("deview-corrupt-documents-").FullName;

    public void Dispose() =>
        Directory.Delete(directory, true);
}
