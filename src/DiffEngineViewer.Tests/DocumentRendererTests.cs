/// <summary>
/// The documents folder as a viewer loads it: DiffEngineViewer.Documents in a load context of its
/// own, from the copy beside these tests, with the natives for whichever platform is running them.
/// <para>
/// No pixel snapshots. Fonts differ from one platform to the next, so what a page looks like is
/// not a fact these could hold. What they hold is what the viewer relies on: how many pages, that
/// each is a png a head can draw, that drawing is deterministic, and that a page which changed is
/// the only one whose bytes do.
/// </para>
/// </summary>
public class DocumentRendererTests :
    IDisposable
{
    [Test]
    public async Task TheFolderIsFoundBesideTheViewer() =>
        await Assert.That(DocumentPlugin.Find()).IsNotNull();

    [Test]
    public async Task APdfReadsPageByPage()
    {
        var text = Plugin.Text(WritePdf("text.pdf", "alpha", "bravo"));
        await Assert.That(text.ReplaceLineEndings("\n")).IsEqualTo(
            """
            --- page 1 ---
            alpha
            --- page 2 ---
            bravo

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task APdfDrawsAPageAPng()
    {
        var pages = Render(WritePdf("pages.pdf", "alpha", "bravo", "charlie"));
        await Assert.That(pages.Count).IsEqualTo(3);
        foreach (var page in pages)
        {
            await Assert.That(ImageHeader.TryRead(page, out var header)).IsTrue();
            // 300 x 200 points at 150 dpi
            await Assert.That(header).IsEqualTo(new ImageHeader(ImageFormat.Png, 625, 417));
        }
    }

    /// <summary>
    /// What lets the viewer say which pages of two documents differ by comparing hashes.
    /// </summary>
    [Test]
    public async Task OnlyThePageThatChangedDrawsDifferently()
    {
        var before = Hashes(Render(WritePdf("before.pdf", "alpha", "bravo", "charlie")));
        var again = Hashes(Render(WritePdf("again.pdf", "alpha", "bravo", "charlie")));
        var after = Hashes(Render(WritePdf("after.pdf", "alpha", "BRAVO", "charlie")));

        await Assert.That(again).IsEquivalentTo(before);
        await Assert.That(after[0]).IsEqualTo(before[0]);
        await Assert.That(after[1]).IsNotEqualTo(before[1]);
        await Assert.That(after[2]).IsEqualTo(before[2]);
    }

    [Test]
    public async Task AWordDocumentReadsAsMarkdown()
    {
        var path = Sample("sample.docx");
        await Assert.That(Plugin.Text(path)).Contains("Hello World!");
        await Assert.That(Render(path)).IsNotEmpty();
    }

    /// <summary>
    /// A family the machine lacks is drawn in a stand-in rather than failing the page. Morph fails
    /// one outright, and Calibri, which most Word documents are set in, is missing from Linux and
    /// from macOS without Office.
    /// </summary>
    [Test]
    public async Task AFontTheMachineLacksIsDrawnInAStandIn()
    {
        var path = Path.Combine(directory, "missing-font.docx");
        File.WriteAllBytes(path, SampleDocx.Build("No Such Family Anywhere", "alpha"));
        await Assert.That(Plugin.Text(path)).Contains("alpha");
        await Assert.That(Render(path)).IsNotEmpty();
    }

    [Test]
    public async Task ASpreadsheetReadsAsMarkdown()
    {
        var path = Sample("sample.xlsx");
        var text = Plugin.Text(path);
        await Assert.That(text).Contains("First Name");
        await Assert.That(text).Contains("Dulce");
        await Assert.That(Render(path)).IsNotEmpty();
    }

    [Test]
    public async Task APresentationReadsAsMarkdown()
    {
        var path = Sample("sample.pptx");
        await Assert.That(Plugin.Text(path)).Contains("Hello, PowerPoint!");
        await Assert.That(Render(path).Count).IsEqualTo(1);
    }

    /// <summary>
    /// A small SVG draws larger than its own size: a head never enlarges a picture, and a 24 pixel
    /// icon cannot be reviewed at 24 pixels.
    /// </summary>
    [Test]
    public async Task AnSvgDrawsAtAReviewableSize()
    {
        var path = Write(
            "logo.svg",
            """
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="12">
              <rect width="24" height="12" fill="red" />
            </svg>
            """);
        var pages = Render(path);
        await Assert.That(pages.Count).IsEqualTo(1);
        await Assert.That(ImageHeader.TryRead(pages[0], out var header)).IsTrue();
        await Assert.That(header).IsEqualTo(new ImageHeader(ImageFormat.Png, 1024, 512));
    }

    /// <summary>
    /// A file that is not what its name says fails, and costs nothing beyond itself: the next
    /// document still draws.
    /// </summary>
    [Test]
    public async Task ACorruptDocumentFailsAlone()
    {
        var corrupt = Write("corrupt.pdf", "not a pdf at all");
        await Assert.That(() => Plugin.Text(corrupt)).Throws<Exception>();
        await Assert.That(() => Render(corrupt)).Throws<Exception>();
        await Assert.That(Render(WritePdf("fine.pdf", "alpha")).Count).IsEqualTo(1);
    }

    /// <summary>
    /// The whole of it as a viewer runs it: a pair read through <see cref="FileSide"/>, then the
    /// watch reading their text and drawing their pages through the real folder, until there is
    /// nothing left to do.
    /// </summary>
    [Test]
    public async Task AViewerReadsAndDrawsAPairOfPdfs()
    {
        using var documents = DocumentPlugin.Find()!;
        var left = WritePdf("received.pdf", "alpha", "bravo");
        var right = WritePdf("verified.pdf", "alpha", "BRAVO");
        var host = new SessionHost(ViewerSession.EnqueueFile(
            SessionState.Start(ViewerMode.File, Fixtures.Columns, Fixtures.Rows),
            QueueEntry.ForFiles(left, right, FileSide.Read(left, documents), FileSide.Read(right, documents))));

        var watch = new DocumentWatch(host, documents);
        while (watch.Pump())
        {
        }

        var state = host.State;
        var screen = ScreenBuilder.Build(state);
        await Assert.That(state.Current!.RightText).Contains("BRAVO");
        await Assert.That(screen.Status).EndsWith("page 2 of 2, page 2 differs");
        await Assert.That(screen.Left.Image).IsNotNull();
        await Assert.That(File.Exists(screen.Left.Image!.Path)).IsTrue();
    }

    static DocumentPlugin Plugin { get; } = DocumentPlugin.Find(Path.Combine(AppContext.BaseDirectory, "documents"))!;

    List<byte[]> Render(string path)
    {
        var pages = Directory.CreateDirectory(Path.Combine(directory, $"{Path.GetFileName(path)}-pages")).FullName;
        var landed = new List<string>();
        var count = Plugin.Render(path, pages, landed.Add);
        if (count != landed.Count)
        {
            throw new($"Rendered {count} pages but announced {landed.Count}.");
        }

        return landed.Select(File.ReadAllBytes).ToList();
    }

    static List<string> Hashes(List<byte[]> pages) =>
        pages
            .Select(_ => Convert.ToHexString(SHA256.HashData(_)))
            .ToList();

    static string Sample(string name) =>
        Path.Combine(AppContext.BaseDirectory, "DocumentSamples", name);

    string WritePdf(string name, params string[] pages)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, SamplePdf.Build(pages));
        return path;
    }

    string Write(string name, string content)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    readonly string directory = Directory.CreateTempSubdirectory("deview-documents-").FullName;

    public void Dispose() =>
        Directory.Delete(directory, true);
}
