extern alias engine;
using System.Globalization;
using EngineLaunch = engine::DiffEngine.LaunchResult;
using EnginePatchMode = engine::DiffEngine.InlinePatchMode;
using EngineResult = engine::DiffEngine.InlineResult;
using EngineRunner = engine::DiffEngine.DiffRunner;
using EngineTool = engine::DiffEngine.DiffTool;
using EngineTools = engine::DiffEngine.DiffTools;

/// <summary>
/// Launches the viewer built from this working tree through the real DiffEngine entry points, for
/// a person to look at.
/// <para>
/// This is the only coverage the launch path has. Everything else stops at the socket:
/// EngineInlineTests drives a stand in server, and the screen tests build a Screen and never start
/// a process. Between them they never answer whether an executable can be found, whether the
/// payload survives the handoff, or whether the right thing appears.
/// </para>
/// <para>
/// What each head draws is the other half of it. Pictures are drawn with each toolkit's own
/// decoder, so which image formats show is per platform, and documents and maps need the documents
/// folder, which a head's bin has and DiffEngine's bundled copy does not. None of that is in a
/// screen snapshot, which stops at the rows every renderer draws.
/// </para>
/// <para>
/// Each case prints what to look for. Accepting an inline snapshot rewrites a file in a temp
/// directory, and the test prints it afterwards, so the outcome is visible rather than taken on
/// trust.
/// </para>
/// <para>
/// Explicit, so an ordinary run never opens a window. The viewer is single instance, so rebuild
/// first: a lingering instance would otherwise answer instead, which <see cref="ManualViewer"/>
/// explains. Run one case, or the class, which opens each in turn as the last is closed:
/// <code>
/// Get-Process DiffEngineViewer -ErrorAction SilentlyContinue | Stop-Process -Force
/// dotnet build src
/// dotnet test --project src/DiffEngineViewer.Tests -- --treenode-filter "/*/*/DiffEngineViewerLauncherTests/InlineQueueFromSeparateLaunches"
/// dotnet test --project src/DiffEngineViewer.Tests -- --treenode-filter "/*/*/DiffEngineViewerLauncherTests/*"
/// </code>
/// </para>
/// <para>
/// On its own port, and with the tray left out, deliberately: a machine running the real
/// DiffEngineTray would otherwise receive these into its live queue and show them in its installed
/// viewer, which is not the code in this working tree.
/// </para>
/// </summary>
[NotInParallel]
public class ViewerLauncherTests
{
    const int port = 3499;

    /// <summary>
    /// Here rather than in the module initializer, so only these tests can produce a window.
    /// </summary>
    [Before(Class)]
    public static void Enable()
    {
        ManualViewer.Enable();
        Environment.SetEnvironmentVariable(ViewerClient.PortVariable, port.ToString(CultureInfo.InvariantCulture));
        EngineRunner.TrayDisabled = true;
        // Five by default, and the class launches one window per extension. Left raised afterwards:
        // the setter pins a value rather than restoring the ambient one, and nothing else in this
        // assembly launches more than a handful.
        EngineRunner.MaxInstancesToLaunch(100);
        // A file derived from a document names no tool: where it goes is decided by resolving the
        // document from its path. This assembly clears DiffEngine_ToolOrder with every other
        // machine setting, and in the default order that follows another tool is ahead of the
        // viewer for most types, so the document was in the viewer and its pages in that tool.
        EngineTools.UseOrder(EngineTool.DiffEngineViewer);
    }

    /// <summary>
    /// The belt to WaitForClose's braces: a case that throws before it gets there would otherwise
    /// leave a hidden viewer to answer the next run.
    /// </summary>
    [After(Class)]
    public static void Cleanup()
    {
        ManualViewer.Close();
        Environment.SetEnvironmentVariable(ViewerClient.PortVariable, null);
        EngineRunner.TrayDisabled = false;
        EngineTools.Reset();
    }

    const string received =
        """
        the quick
        brown dog
        jumps over
        """;

    const string expected =
        """
        the quick
        brown fox
        jumps over
        """;

    [Test]
    [Explicit]
    public async Task FileDiff()
    {
        using var directory = new TempDirectory();
        var temp = Write(directory, "Sample.received.txt", received);
        var target = Write(directory, "Sample.verified.txt", expected);

        ManualViewer.Expect(
            "Two file diff",
            "Two panes, headers Sample.received.txt and Sample.verified.txt",
            "Line 2 highlighted on both sides, dog on the left and fox on the right",
            "No pending queue column",
            "Buttons are Accept, Close, Prev change, Next change and Changes only");

        await EngineRunner.LaunchAsync(EngineTool.DiffEngineViewer, temp, target);
        await ManualViewer.WaitForClose();
    }

    /// <summary>
    /// Forty lines with changes at 3, 17 and 33, so scrolling and next/previous change have
    /// something to land on both inside and outside the first viewport.
    /// </summary>
    [Test]
    [Explicit]
    public async Task FileDiffLongEnoughToScroll()
    {
        using var directory = new TempDirectory();
        var temp = Write(directory, "Long.received.txt", Long(changed: true));
        var target = Write(directory, "Long.verified.txt", Long(changed: false));

        ManualViewer.Expect(
            "Scrolling and change navigation",
            "Arrow keys, PgUp, PgDn, Home and End all scroll",
            "The status line tracks the visible range, ending in of 40",
            "n jumps forward through changes at lines 3, 17 and 33, p jumps back",
            "The mouse wheel scrolls");

        await EngineRunner.LaunchAsync(EngineTool.DiffEngineViewer, temp, target);
        await ManualViewer.WaitForClose();
    }

    /// <summary>
    /// The common inline case: a literal exists and the snapshot changed.
    /// </summary>
    [Test]
    [Explicit]
    public async Task InlineReplacesALiteral()
    {
        using var directory = new TempDirectory();
        var source = WriteSource(directory, "SampleTests.cs", "\"old value\"");

        ManualViewer.Expect(
            "Inline, existing literal",
            "Title is SampleTests.cs:6, subtitle says inline",
            "Left pane shows new value, right pane shows old value",
            "Accept rewrites the literal in the source file");

        var result = await EngineRunner.AddInlineAsync(
            Patch(source, 6, "\"old value\"", "new value"));

        await Assert.That(result).IsEqualTo(EngineResult.Queued);
        await ManualViewer.WaitForClose();
        Report(source);
    }

    /// <summary>
    /// The other half of the inline story: no literal yet, so accepting appends the call rather
    /// than replacing an argument.
    /// </summary>
    [Test]
    [Explicit]
    public async Task InlineAppendsToACallWithNoSnapshot()
    {
        using var directory = new TempDirectory();
        var source = WriteSource(directory, "NewTests.cs", null);

        ManualViewer.Expect(
            "Inline, new snapshot",
            "Right pane header says expected (new snapshot) and the pane is empty",
            "Left pane rows are green and marked +",
            "Accept adds a .Snapshot(...) call after the verify call");

        var result = await EngineRunner.AddInlineAsync(
            Patch(source, 6, null, "brand new", EnginePatchMode.Append));

        await Assert.That(result).IsEqualTo(EngineResult.Queued);
        await ManualViewer.WaitForClose();
        Report(source);
    }

    /// <summary>
    /// Three launches, one window. The first binds the port and the rest hand their patch over and
    /// exit, which is the behaviour a failing test run depends on.
    /// </summary>
    [Test]
    [Explicit]
    public async Task InlineQueueFromSeparateLaunches()
    {
        using var directory = new TempDirectory();
        var first = WriteSource(directory, "FirstTests.cs", "\"old value\"");
        var second = WriteSource(directory, "SecondTests.cs", "\"other value\"");
        var third = WriteSource(directory, "ThirdTests.cs", null);

        ManualViewer.Expect(
            "Three pending snapshots in one window",
            "Only one window opens, not three",
            "Pending (3) column lists all three files, the first selected",
            "Clicking an entry switches panes, Tab and Shift+Tab move between them",
            "Accept all is enabled and accepts every one",
            "The window closes itself once the queue empties");

        foreach (var (source, expression, content) in new[]
                 {
                     (first, "\"old value\"", "first new"),
                     (second, "\"other value\"", "second new"),
                     (third, null, "third new")
                 })
        {
            var mode = expression is null ? EnginePatchMode.Append : EnginePatchMode.Set;
            var result = await EngineRunner.AddInlineAsync(Patch(source, 6, expression, content, mode));
            await Assert.That(result).IsEqualTo(EngineResult.Queued);
        }

        await ManualViewer.WaitForClose();
        Report(first, second, third);
    }

    /// <summary>
    /// A snapshot big enough that the panes scroll, which the inline path reaches differently from
    /// file mode: the content comes over stdin rather than off disk.
    /// </summary>
    [Test]
    [Explicit]
    public async Task InlineLongEnoughToScroll()
    {
        using var directory = new TempDirectory();
        var source = WriteSource(directory, "LongTests.cs", "\"old value\"");

        ManualViewer.Expect(
            "Inline with a long snapshot",
            "Forty rows on the left, scrollable",
            "The status line ends in of 40",
            "A scrollbar on the right, draggable, that follows the keys and the wheel",
            "Dragging it to the bottom lands on the last page and stays there, no spring back",
            "Accept writes the whole thing as a raw string literal");

        var result = await EngineRunner.AddInlineAsync(
            Patch(source, 6, "\"old value\"", Long(changed: true)));

        await Assert.That(result).IsEqualTo(EngineResult.Queued);
        await ManualViewer.WaitForClose();
        Report(source);
    }

    /// <summary>
    /// Discard has to leave the file exactly as it was, which is easy to get wrong and invisible
    /// unless someone looks.
    /// </summary>
    [Test]
    [Explicit]
    public async Task InlineDiscardLeavesTheSourceAlone()
    {
        using var directory = new TempDirectory();
        var source = WriteSource(directory, "DiscardTests.cs", "\"old value\"");
        var before = await File.ReadAllTextAsync(source);

        ManualViewer.Expect(
            "Discard",
            "Press Discard, or d",
            "The window closes because the queue is empty");

        await EngineRunner.AddInlineAsync(Patch(source, 6, "\"old value\"", "new value"));
        await ManualViewer.WaitForClose();

        await Assert.That(await File.ReadAllTextAsync(source)).IsEqualTo(before);
        Console.WriteLine("Source unchanged, as it should be after a discard.");
    }

    /// <summary>
    /// Everything the queue column gained in one window: solution headers, a test sub-group, a
    /// conflicted entry with its variant button, and a per-kind accept label is out of reach here
    /// because moves and deletes need a tray owner.
    /// </summary>
    [Test]
    [Explicit]
    public async Task GroupedQueue()
    {
        using var root = new TempDirectory();
        var solutionA = root.Info.CreateSubdirectory("SolutionA");
        await File.WriteAllTextAsync(Path.Combine(solutionA.FullName, "SolutionA.slnx"), "");
        var solutionB = root.Info.CreateSubdirectory("SolutionB");
        await File.WriteAllTextAsync(Path.Combine(solutionB.FullName, "SolutionB.slnx"), "");

        var grouped = WriteSource(solutionA, "ATests.cs", "\"old value\"");
        var single = WriteSource(solutionA, "OtherTests.cs", "\"other value\"");
        var conflicted = WriteSource(solutionB, "BTests.cs", "\"framework value\"");

        ManualViewer.Expect(
            "Grouped queue with a conflict",
            "SolutionA (3) and SolutionB (1) headers, dimmed, with the entries indented",
            "Compare handles nulls (2) sub-header over the two ATests.cs call sites",
            "Order is stable labels the OtherTests.cs entry by test name",
            "The BTests.cs entry is marked *, its pane header says received (net8.0)",
            "The Variant 1/2: net8.0 button (or v) flips to net9.0 and back",
            "Right-clicking BTests.cs selects it and offers Accept, Show next variant, Discard, Open source file",
            "Right-clicking the SolutionA header offers Accept all in SolutionA and Discard all in SolutionA, sweeping only that solution",
            "On Linux the viewer draws the menu, and any other click or key closes it",
            "On Windows and macOS it is the real OS one: arrows and Enter drive it, Escape closes it WITHOUT closing the window, and it flips rather than clips near the edge of a screen",
            "On macOS there is a menu bar, Quit hides rather than kills when the tray is running, and Close and Minimize work",
            "Clicking the SolutionA header folds it, its marker flips - to +, and its count stays",
            "The selection moves to a visible entry rather than disappearing under the fold",
            "Tab steps over the folded entries, and Accept all still takes them",
            "Hovering an entry shows its full path, and the frameworks when marked *",
            "Hovering a header, or a row whose label already says everything, shows no tooltip",
            "Accepting the conflicted entry writes the variant on screen",
            "Accept all skips the conflict and says 1 conflict needs review");

        foreach (var patch in new[]
                 {
                     new(grouped, 6, "\"old value\"", "first new")
                     {
                         TestName = "Compare handles nulls"
                     },
                     new(grouped, 7, "\"old value\"", "second new")
                     {
                         TestName = "Compare handles nulls"
                     },
                     new(single, 6, "\"other value\"", "other new")
                     {
                         TestName = "Order is stable"
                     },
                     Patch(conflicted, 6, "\"framework value\"", "eight", framework: "net8.0"),
                     Patch(conflicted, 6, "\"framework value\"", "nine", framework: "net9.0")
                 })
        {
            var result = await EngineRunner.AddInlineAsync(patch);
            await Assert.That(result).IsEqualTo(EngineResult.Queued);
        }

        await ManualViewer.WaitForClose();
        Report(grouped, single, conflicted);
    }

    /// <summary>
    /// Enough pending snapshots that accepting them takes a while, for watching an accept-all go:
    /// the status line counting up, entries leaving as they land, and the window answering the whole
    /// time. Two solutions of ten classes, twenty five snapshots a class, which is the shape a run
    /// that changed something widely used leaves behind. Every accept rewrites a file holding two
    /// dozen others, and the multi line literal it writes moves every call site below it, so the
    /// rest of the class is found by what it says rather than by the line it was reported on.
    /// <para>
    /// A fast disk accepts all of that in a second or two, which is not long enough to watch or to
    /// try anything while it runs. So the class halfway down the batch is held the way another
    /// process part way through writing it would hold it - an IDE accepting its own snapshot - and
    /// the batch stops there for a few seconds. That is also the case the window used to freeze in.
    /// </para>
    /// <para>
    /// Three conflicts ride along. Accept-all leaves those for review, so the window is still open
    /// once the batch has finished, and what it says about the batch can be read.
    /// </para>
    /// </summary>
    [Test]
    [Explicit]
    public async Task AcceptAllOverALongQueue()
    {
        const int classes = 10;
        const int snapshots = 25;
        const int conflicts = 3;
        using var root = new TempDirectory();
        var solutionA = root.Info.CreateSubdirectory("SolutionA");
        await File.WriteAllTextAsync(Path.Combine(solutionA.FullName, "SolutionA.slnx"), "");
        var solutionB = root.Info.CreateSubdirectory("SolutionB");
        await File.WriteAllTextAsync(Path.Combine(solutionB.FullName, "SolutionB.slnx"), "");

        var sources = new List<string>();
        var patches = new List<engine::DiffEngine.InlinePatch>();
        foreach (var solution in new[] { solutionA, solutionB })
        {
            for (var index = 1; index <= classes; index++)
            {
                var (source, lines) = WriteClass(solution, $"{solution.Name[^1]}{index:D2}Tests.cs", snapshots);
                sources.Add(source);
                for (var method = 1; method <= snapshots; method++)
                {
                    patches.Add(
                        new(source, lines[method - 1], $"\"old {method}\"", Snapshot(method))
                        {
                            TestName = null,
                            MemberName = $"Case{method}"
                        });
                }
            }
        }

        var (conflicted, conflictedLines) = WriteClass(solutionB, "ConflictedTests.cs", conflicts);
        for (var method = 1; method <= conflicts; method++)
        {
            var line = conflictedLines[method - 1];
            patches.Add(Patch(conflicted, line, $"\"old {method}\"", $"eight {method}", framework: "net8.0"));
            patches.Add(Patch(conflicted, line, $"\"old {method}\"", $"nine {method}", framework: "net9.0"));
        }

        var total = sources.Count * snapshots;
        ManualViewer.Expect(
            "Accept all over a long queue",
            $"Pending ({total + conflicts}), grouped under SolutionA and SolutionB headers - the list follows the last arrival to the bottom, so scroll it up to see them",
            "Press Accept all, or Shift+A",
            $"The status line counts up - Accepting 1 of {total}, 2 of {total} - and entries leave the list as they land",
            "Accept, Discard and Accept all are disabled until it finishes, and a, d and Shift+A do nothing",
            "About halfway the count stops for six seconds, on a class this test is holding the way another process writing it would",
            "The window keeps answering the whole time, stop included: scroll, Tab through the entries, click one, fold a header",
            "The count carries on once the class is let go",
            $"When it finishes the {conflicts} ConflictedTests.cs entries are left, and the status line says Accepted {total}, {conflicts} conflicts need review");

        foreach (var patch in patches)
        {
            var result = await EngineRunner.AddInlineAsync(patch);
            await Assert.That(result).IsEqualTo(EngineResult.Queued);
        }

        // The batch goes in the order the queue lists, so the class listed halfway down is the
        // one it reaches halfway through
        if (!ViewerClient.TrySend(new(ViewerVerb.List), out var listing, port))
        {
            throw new("The viewer did not list its queue.");
        }

        var halfway = listing.Items[listing.Items.Count / 2].Name.Split(':')[0];
        using var cancel = new CancelSource();
        var holding = HoldOnceReached(sources.Single(_ => Path.GetFileName(_) == halfway), port, cancel.Token);

        await ManualViewer.WaitForClose();
        await cancel.CancelAsync();
        await holding;

        var left = sources.Sum(Unaccepted);
        Console.WriteLine();
        Console.WriteLine($"{total - left} of {total} call sites rewritten, and {Unaccepted(conflicted)} of {conflicts} conflicted ones left alone.");
        // One class in full, for what the accepts wrote. The other nineteen are the same shape.
        Report(sources[0]);
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
            "The status line says the two are different files",
            "+ or Zoom in, or the wheel over a picture, enlarges both a step, and the status line says zoom 150%",
            "Enlarged past the space, dragging one picture moves both, and neither is drawn over the rows above",
            "- or Zoom out steps back, and 0 goes straight to fitted",
            "Right-clicking either pane offers Copy all, which copies that side's rows");

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
            "In Picture only, Prev change and Next change move between the pages that differ",
            "The view left on is the one the next PDF opens in, in this run and the next",
            "+ and - zoom the page, the wheel over it does too, and turning the page keeps the zoom and the place",
            "The wheel over the text still scrolls the text",
            "Right-clicking the text offers Copy selection once some is selected, Copy all and Select all");

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
            "r cycles the three views",
            "j, or the Projection button, draws both sides in the next projection, the button naming the one on screen",
            "Going round to one already drawn shows it at once, and the one left on is the one the next map opens in");

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
            "No page buttons, and the status line says the drawings differ",
            "j, or the Projection button, draws both sides in the next projection");

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
            pairs.Add(($"Letter{round}", ".docx", Edited(".docx", "Hello World!", $"Hello World {round}!"), await File.ReadAllBytesAsync(Sample(".docx"))));
            pairs.Add(($"Sheet{round}", ".xlsx", Edited(".xlsx", "Dulce", $"Dulce {round}"), await File.ReadAllBytesAsync(Sample(".xlsx"))));
            pairs.Add(($"Slides{round}", ".pptx", Edited(".pptx", "Hello, PowerPoint!", $"Hello, PowerPoint {round}!"), await File.ReadAllBytesAsync(Sample(".pptx"))));
            pairs.Add(($"Survey{round}", ".fgb", BusyMap(round, moved: true), BusyMap(round, moved: false)));
            pairs.Add(($"Photo{round}", ".jpg", SampleImages.Photo(220, 120, 60), SampleImages.Photo(60, 120, 220)));
            pairs.Add(($"Logo{round}", ".svg", Encoding.UTF8.GetBytes(SvgOf("red")), Encoding.UTF8.GetBytes(SvgOf("blue"))));
        }

        foreach (var extension in pairs.Select(_ => _.Extension).Distinct())
        {
            await Assert.That(EngineTools.IsDetectedForExtension(EngineTool.DiffEngineViewer, extension)).IsTrue();
        }

        using var directory = new TempDirectory();
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
            "The 4000 by 3000 photos show a spinner briefly while they are decoded and scaled, and resizing the window rescales them without it stalling",
            "Maximise the window, close it and run this again: it opens maximised, and restoring it goes back to the size it had before");

        foreach (var (name, extension, received, verified) in pairs)
        {
            var temp = Path.Combine(directory, $"{name}.received{extension}");
            var target = Path.Combine(directory, $"{name}.verified{extension}");
            await File.WriteAllBytesAsync(temp, received);
            await File.WriteAllBytesAsync(target, verified);
            await EngineRunner.LaunchAsync(EngineTool.DiffEngineViewer, temp, target);
        }

        await ManualViewer.WaitForClose();
    }

    /// <summary>
    /// A received file that is not the document its extension says, one of every kind, beside a
    /// verified one that is whole: what a test that failed part way through writing its snapshot
    /// leaves behind.
    /// </summary>
    [Test]
    [Explicit]
    public async Task DamagedDocuments()
    {
        string[] extensions =
        [
            ".pdf", ".docx", ".xlsx", ".pptx", ".svg",
            ".geojson", ".topojson", ".kml", ".gpx", ".wkt",
            ".kmz", ".wkb", ".fgb", ".geoparquet"
        ];
        using var directory = new TempDirectory();
        ManualViewer.Expect(
            "Damaged documents",
            $"One window, Pending ({extensions.Length + 1}), a pair of each kind with its received file cut in half, and an empty PDF last",
            "The window opens and stays answering on every entry: none of them closes it or leaves it waiting",
            "A PDF, an Office file or a binary map shows its format and bytes rather than text, and the status line says once that it could not be read: Not a readable Word document, and why",
            "An SVG or a text map shows the half of its text there is, and the status line says it could not be drawn",
            "The left header says (not drawn), with no spinner left turning",
            "The verified side, which is whole, is still drawn on the right",
            "The empty PDF says the file is empty",
            "r cycles the views on each, the status line saying what that view could not do");

        foreach (var extension in extensions)
        {
            var whole = Whole(extension);
            await Pair(directory, $"Damaged{extension.TrimStart('.')}", extension, whole[..(whole.Length / 2)], whole);
        }

        await Pair(directory, "Empty", ".pdf", [], Whole(".pdf"));
        await ManualViewer.WaitForClose();
    }

    /// <summary>
    /// The first run of a snapshot: a received file and nothing verified beside it, for text and
    /// for every other type the viewer is offered. What DiffEngine does about the missing file is
    /// decided per extension before the viewer hears of the pair, and it used to write a
    /// placeholder there, or give up where it had none to write, which was most of the maps.
    /// </summary>
    [Test]
    [Explicit]
    [Arguments(".txt")]
    [Arguments(".png")]
    [Arguments(".jpg")]
    [Arguments(".pdf")]
    [Arguments(".docx")]
    [Arguments(".xlsx")]
    [Arguments(".pptx")]
    [Arguments(".svg")]
    [Arguments(".geojson")]
    [Arguments(".topojson")]
    [Arguments(".kml")]
    [Arguments(".gpx")]
    [Arguments(".wkt")]
    [Arguments(".kmz")]
    [Arguments(".wkb")]
    [Arguments(".fgb")]
    [Arguments(".geoparquet")]
    public async Task NewSnapshot(string extension)
    {
        await Assert.That(EngineTools.IsDetectedForExtension(EngineTool.DiffEngineViewer, extension)).IsTrue();

        using var directory = new TempDirectory();
        var temp = Path.Combine(directory, $"Sample.received{extension}");
        var target = Path.Combine(directory, $"Sample.verified{extension}");
        var image = extension is ".png" or ".jpg";
        await File.WriteAllBytesAsync(temp, image ? SampleImages.Build(extension, 220, 40, 40) : Whole(extension));

        ManualViewer.Expect(
            $"New snapshot {extension}",
            $"Headers Sample.received{extension} and Sample.verified{extension}",
            "The right pane is empty, with no picture, no page and no spinner left turning",
            NewSnapshotLeftPane(extension),
            $"The status line says only Sample.received{extension} exists, and nothing about a file that could not be read",
            $"No Sample.verified{extension} is written beside the received file until Accept is pressed");

        await EngineRunner.LaunchAsync(EngineTool.DiffEngineViewer, temp, target);
        await ManualViewer.WaitForClose();
    }

    static string NewSnapshotLeftPane(string extension)
    {
        if (extension == ".txt")
        {
            return "The left pane has all three lines, each marked added";
        }

        if (extension is ".png" or ".jpg")
        {
            return "The left pane has the picture's rows, each marked added, and the picture under them";
        }

        return "The left pane has the document's text, every line marked added, and its drawing under it";
    }

    /// <summary>
    /// What a snapshot library reports for a document it also split into pages, in the order it
    /// reports it: a delete that stands alone, the moves that stand alone with the document among
    /// them, the moves derived from the document, then the delete derived from it.
    /// <para>
    /// The document is one the viewer draws, so nothing is opened for what was derived from it:
    /// each is tracked and shown beneath the document, and goes where the document goes.
    /// </para>
    /// </summary>
    [Test]
    [Explicit]
    public async Task DocumentWithDerivedFiles()
    {
        await Assert.That(EngineTools.IsDetectedForExtension(EngineTool.DiffEngineViewer, ".pdf")).IsTrue();

        using var directory = new TempDirectory();
        string InDirectory(string name) => Path.Combine(directory, name);

        var orphan = InDirectory("Orphan.verified.txt");
        await File.WriteAllTextAsync(orphan, "a snapshot no test produces any more");

        var notesTemp = InDirectory("Notes.received.txt");
        var notesTarget = InDirectory("Notes.verified.txt");
        await File.WriteAllTextAsync(notesTemp, "notes, changed");
        await File.WriteAllTextAsync(notesTarget, "notes");

        var document = InDirectory("Report.received.pdf");
        var documentTarget = InDirectory("Report.verified.pdf");
        await File.WriteAllBytesAsync(document, SamplePdf.Build("alpha", "BRAVO", "charlie"));
        await File.WriteAllBytesAsync(documentTarget, SamplePdf.Build("alpha", "bravo", "charlie"));

        var infoTemp = InDirectory("Report.received.txt");
        var infoTarget = InDirectory("Report.verified.txt");
        await File.WriteAllTextAsync(infoTemp, "Pages: 3\nTitle: Report, revised");
        await File.WriteAllTextAsync(infoTarget, "Pages: 4\nTitle: Report");

        var pageTemp = InDirectory("Report#page_0002.received.png");
        var pageTarget = InDirectory("Report#page_0002.verified.png");
        await File.WriteAllBytesAsync(pageTemp, SampleImages.Build(".png", 220, 40, 40));
        await File.WriteAllBytesAsync(pageTarget, SampleImages.Build(".png", 40, 80, 220));

        // New: the text of the page, which nothing has been verified for yet
        var pageTextTemp = InDirectory("Report#page_0002.received.txt");
        var pageTextTarget = InDirectory("Report#page_0002.verified.txt");
        await File.WriteAllTextAsync(pageTextTemp, "BRAVO");

        // A page the document no longer has
        var lostPage = InDirectory("Report#page_0004.verified.png");
        await File.WriteAllBytesAsync(lostPage, SampleImages.Build(".png", 40, 80, 220));

        ManualViewer.Expect(
            "A document with files derived from it",
            "One window, Pending (7), with three rows: Orphan.verified.txt, Notes (txt) and + Report (pdf)",
            "The four files derived from the report have no rows of their own, and no other window or tool opened for any of them",
            "Report (pdf) is drawn as a document: its text above, page 2 differing, and the page under it",
            "With the report selected the buttons read Accept move +4 and Discard +4",
            "Its tooltip says 4 files derived from it are accepted or discarded with it",
            "Clicking the selected report's row, or its menu, unfolds it: (txt), #page_0002 (png), #page_0002 (txt) and #page_0004.verified.png beneath it, in that order",
            "#page_0002 (png) shows the two pictures, red against blue, and its accept is of that file alone",
            "#page_0002 (txt) has nothing on the right, and no Report#page_0002.verified.txt was written beside it",
            "#page_0004.verified.png is a delete, as Orphan.verified.txt is",
            "Stepping through the queue steps over them while folded and into them once unfolded",
            "Accepting the report takes all five: the four moves' verified files are written, the page 4 file is gone, and Pending (2) is left",
            "Orphan.verified.txt and Notes (txt) are untouched by that, each accepted on its own");

        // The order Verify reports in. A delete that stands alone has no source to wait for
        await EngineRunner.AddDeleteAsync(orphan);

        await EngineRunner.LaunchAsync(EngineTool.DiffEngineViewer, notesTemp, notesTarget);
        await EngineRunner.LaunchAsync(EngineTool.DiffEngineViewer, document, documentTarget);

        // After the source, which is what they are shown beneath
        EngineLaunch[] derived =
        [
            await EngineRunner.LaunchDerivedForTextAsync(infoTemp, infoTarget, document, null),
            await EngineRunner.LaunchDerivedAsync(pageTemp, pageTarget, document, null),
            await EngineRunner.LaunchDerivedForTextAsync(pageTextTemp, pageTextTarget, document, null)
        ];
        await EngineRunner.AddDerivedDeleteAsync(lostPage, document);

        await StayedInTheViewer(derived);
        await ManualViewer.WaitForClose();
    }

    /// <summary>
    /// The documents that are not a PDF, each of which the viewer reads and draws its own way, and
    /// <see cref="DocumentWithDerivedFiles"/> for each: the files derived from one are beneath it
    /// whichever it is.
    /// </summary>
    [Test]
    [Explicit]
    [Arguments(".docx", "Hello World!")]
    [Arguments(".xlsx", "Dulce")]
    [Arguments(".pptx", "Hello, PowerPoint!")]
    public async Task OfficeDocumentWithDerivedFiles(string extension, string text)
    {
        await Assert.That(EngineTools.IsDetectedForExtension(EngineTool.DiffEngineViewer, extension)).IsTrue();

        using var directory = new TempDirectory();
        var document = WritePair(directory, "Sample", extension, Edited(extension, text, $"{text} CHANGED"), await File.ReadAllBytesAsync(Sample(extension)));
        var info = WritePair(directory, "Sample", ".txt", Utf8("Pages: 1\nRevision: 2"), Utf8("Pages: 1\nRevision: 1"));
        var page = WritePair(directory, "Sample#page_0001", ".png", ReceivedPage, VerifiedPage);
        var pageText = WritePair(directory, "Sample#page_0001", ".txt", Utf8($"{text} CHANGED"), Utf8(text));

        var type = extension.TrimStart('.');
        ManualViewer.Expect(
            $"A {type} with files derived from it",
            $"One window, Pending (4), with one row: + Sample ({type})",
            $"Headers Sample.received{extension} and Sample.verified{extension}",
            "The status line says reading text, then the line range",
            $"The document as Markdown above, one line differing: {text} CHANGED on the left, and the page drawn under it",
            "r cycles the three views, and the page buttons turn pages where there is more than one",
            "No other window or tool opened for the three files derived from it",
            "The buttons read Accept move +3 and Discard +3",
            "Unfolded: (txt), #page_0001 (png) and #page_0001 (txt) beneath it",
            "#page_0001 (png) shows the two pictures, red against blue",
            "Accepting the document takes all four and leaves the queue empty");

        await Source(document);
        await StayedInTheViewer(
            await Derived(info, document),
            await Derived(page, document),
            await Derived(pageText, document));
        await ManualViewer.WaitForClose();
    }

    /// <summary>
    /// The first run of a test that verifies a document: the document, its info and every page of
    /// it are received, and nothing is verified. The whole of it is one row and one accept.
    /// </summary>
    [Test]
    [Explicit]
    public async Task NewDocumentWithDerivedFiles()
    {
        await Assert.That(EngineTools.IsDetectedForExtension(EngineTool.DiffEngineViewer, ".pdf")).IsTrue();

        using var directory = new TempDirectory();
        string[] pages = ["alpha", "bravo", "charlie"];
        var document = WritePair(directory, "Report", ".pdf", SamplePdf.Build(pages), null);
        var files = new List<(string Temp, string Target)>
        {
            WritePair(directory, "Report", ".txt", Utf8("Pages: 3\nTitle: Report"), null)
        };
        for (var index = 0; index < pages.Length; index++)
        {
            var name = $"Report#page_{index + 1:D4}";
            files.Add(WritePair(directory, name, ".png", ReceivedPage, null));
            files.Add(WritePair(directory, name, ".txt", Utf8(pages[index]), null));
        }

        ManualViewer.Expect(
            "A new document with files derived from it",
            "One window, Pending (8), with one row: + Report (pdf)",
            "The left pane has the document's text, every line marked added, and its pages under it; the right pane is empty",
            "The status line says only Report.received.pdf exists",
            "The buttons read Accept move +7 and Discard +7",
            "Unfolded: (txt), then a (png) and a (txt) for each of the three pages, in page order",
            "Each of them has nothing on the right, and none has a verified file written beside it",
            "Accepting the report writes all eight verified files and leaves the queue empty",
            "Run it again and Discard instead: all eight received files go, and nothing verified is written");

        await Source(document);
        var derived = new List<EngineLaunch>();
        foreach (var file in files)
        {
            derived.Add(await Derived(file, document));
        }

        await StayedInTheViewer([.. derived]);
        await ManualViewer.WaitForClose();
    }

    /// <summary>
    /// A container whose own file passed, with two of the documents in it changed: a mail and its
    /// attachments. Each attachment is then the outermost source that is pending, so its pages
    /// name it, and each is a document with its own files beneath it.
    /// </summary>
    [Test]
    [Explicit]
    public async Task AttachmentsOfAnUnchangedMail()
    {
        using var directory = new TempDirectory();
        var pdf = WritePair(directory, "Mail#Attachment1", ".pdf", SamplePdf.Build("alpha", "BRAVO", "charlie"), SamplePdf.Build("alpha", "bravo", "charlie"));
        var pdfPage = WritePair(directory, "Mail#Attachment1.page_0002", ".png", ReceivedPage, VerifiedPage);
        var pdfPageText = WritePair(directory, "Mail#Attachment1.page_0002", ".txt", Utf8("BRAVO"), Utf8("bravo"));
        var docx = WritePair(directory, "Mail#Attachment2", ".docx", Edited(".docx", "Hello World!", "Hello World! CHANGED"), await File.ReadAllBytesAsync(Sample(".docx")));
        var docxPage = WritePair(directory, "Mail#Attachment2.page_0001", ".png", ReceivedPage, VerifiedPage);

        ManualViewer.Expect(
            "Attachments of an unchanged mail",
            "One window, Pending (5), with two rows: + Mail#Attachment1 (pdf) and + Mail#Attachment2 (docx)",
            "Mail#Attachment1 (pdf) is on screen, drawn as a document, with Accept move +2",
            "Unfolded: .page_0002 (png) and .page_0002 (txt) beneath it",
            "Mail#Attachment2 (docx) reads Accept move +1, with .page_0001 (png) beneath it",
            "Neither document has the other's files beneath it",
            "Accepting the first takes its three files and leaves the second with its two");

        // Both sources ahead of anything derived from either, as they are reported
        await Source(pdf);
        await Source(docx);
        await StayedInTheViewer(
            await Derived(pdfPage, pdf),
            await Derived(pdfPageText, pdf),
            await Derived(docxPage, docx));
        await ManualViewer.WaitForClose();
    }

    /// <summary>
    /// The same mail with its own file changed too. It is then the outermost source that is
    /// pending, and one level is all a sender says, so the attachments and their pages all name
    /// the mail. A mail is not a document the viewer draws, so none of them is beneath anything:
    /// an attachment is still drawn as the document it is, with its pages as rows beside it, and
    /// an attachment the mail has lost is the ordinary delete it also is.
    /// </summary>
    [Test]
    [Explicit]
    public async Task AttachmentsOfAChangedMail()
    {
        using var directory = new TempDirectory();
        var mail = WritePair(directory, "Mail", ".eml", Utf8("Subject: Report, revised\n\nSee attached."), Utf8("Subject: Report\n\nSee attached."));
        var pdf = WritePair(directory, "Mail#Attachment1", ".pdf", SamplePdf.Build("alpha", "BRAVO", "charlie"), SamplePdf.Build("alpha", "bravo", "charlie"));
        var pdfPage = WritePair(directory, "Mail#Attachment1.page_0002", ".png", ReceivedPage, VerifiedPage);
        var pdfPageText = WritePair(directory, "Mail#Attachment1.page_0002", ".txt", Utf8("BRAVO"), Utf8("bravo"));
        var docx = WritePair(directory, "Mail#Attachment2", ".docx", Edited(".docx", "Hello World!", "Hello World! CHANGED"), await File.ReadAllBytesAsync(Sample(".docx")));
        var docxPage = WritePair(directory, "Mail#Attachment2.page_0001", ".png", ReceivedPage, VerifiedPage);

        // An attachment the mail no longer has
        var lost = Path.Combine(directory, "Mail#Attachment3.verified.png");
        await File.WriteAllBytesAsync(lost, VerifiedPage);

        ManualViewer.Expect(
            "Attachments of a changed mail",
            "One window, Pending (7), every one of them a row of its own",
            "Mail (eml) has no + beside it and no count, and its buttons read Accept move and Discard with nothing added",
            "Mail#Attachment1 (pdf) is drawn as a document, text and pages, and it too has nothing beneath it",
            "Its page, Mail#Attachment1.page_0002 (png), is a row beside it showing the two pictures",
            "Mail#Attachment2 (docx) and its page are the same",
            "Mail#Attachment3.verified.png is a delete, with a row of its own",
            "No window or tool other than this one opened for any of them",
            "Accepting Mail (eml) takes that file alone, and leaves Pending (6)");

        await Source(mail);
        await StayedInTheViewer(
            await Derived(pdf, mail),
            await Derived(pdfPage, mail),
            await Derived(pdfPageText, mail),
            await Derived(docx, mail),
            await Derived(docxPage, mail));
        await EngineRunner.AddDerivedDeleteAsync(lost, mail.Temp);
        await ManualViewer.WaitForClose();
    }

    /// <summary>
    /// A document rendered to another, which is split into pages in its turn: a Word file saved as
    /// a PDF. The PDF is a source too, and what it and its pages name is the Word file, the
    /// outermost that is pending and the one the test verified.
    /// </summary>
    [Test]
    [Explicit]
    public async Task DocumentRenderedToAnother()
    {
        await Assert.That(EngineTools.IsDetectedForExtension(EngineTool.DiffEngineViewer, ".docx")).IsTrue();

        using var directory = new TempDirectory();
        var docx = WritePair(directory, "Letter", ".docx", Edited(".docx", "Hello World!", "Hello World! CHANGED"), await File.ReadAllBytesAsync(Sample(".docx")));
        var pdf = WritePair(directory, "Letter", ".pdf", SamplePdf.Build("Hello World! CHANGED"), SamplePdf.Build("Hello World!"));
        var page = WritePair(directory, "Letter#page_0001", ".png", ReceivedPage, VerifiedPage);

        ManualViewer.Expect(
            "A document rendered to another",
            "One window, Pending (3), with one row: + Letter (docx)",
            "The buttons read Accept move +2 and Discard +2",
            "Unfolded: (pdf) and #page_0001 (png) beneath it, the page beneath the Word file and not beneath the PDF",
            "Selecting (pdf) draws the PDF as a document, text and page, and its accept is of that file alone",
            "Accepting Letter (docx) takes all three");

        await Source(docx);
        await StayedInTheViewer(
            await Derived(pdf, docx),
            await Derived(page, docx));
        await ManualViewer.WaitForClose();
    }

    /// <summary>
    /// The one thing these cases check for themselves. A file derived from another names no tool,
    /// so where it goes is DiffEngine's to work out, and one that went to some other tool opened a
    /// window there: a new instance, where a file handed to the viewer on screen is a refresh.
    /// Without this the case would carry on, with a checklist for a window that is missing rows.
    /// </summary>
    static async Task StayedInTheViewer(params EngineLaunch[] derived) =>
        await Assert.That(derived.All(_ => _ == EngineLaunch.AlreadyRunningAndSupportsRefresh)).IsTrue();

    static byte[] ReceivedPage => SampleImages.Build(".png", 220, 40, 40);
    static byte[] VerifiedPage => SampleImages.Build(".png", 40, 80, 220);

    static byte[] Utf8(string content) =>
        Encoding.UTF8.GetBytes(content);

    /// <summary>
    /// A received file and, unless it is new, the verified file beside it.
    /// </summary>
    static (string Temp, string Target) WritePair(DirectoryInfo directory, string name, string extension, byte[] received, byte[]? verified)
    {
        var temp = Path.Combine(directory.FullName, $"{name}.received{extension}");
        var target = Path.Combine(directory.FullName, $"{name}.verified{extension}");
        File.WriteAllBytes(temp, received);
        if (verified is not null)
        {
            File.WriteAllBytes(target, verified);
        }

        return (temp, target);
    }

    /// <summary>
    /// A file that stands alone: named to the viewer, as every launch in this class is.
    /// </summary>
    static Task<EngineLaunch> Source((string Temp, string Target) file) =>
        EngineRunner.LaunchAsync(EngineTool.DiffEngineViewer, file.Temp, file.Target);

    /// <summary>
    /// A file derived from <paramref name="source"/>, by the call Verify makes for it. Neither
    /// takes a tool: where the file goes follows from where the source went.
    /// </summary>
    static Task<EngineLaunch> Derived((string Temp, string Target) file, (string Temp, string Target) source)
    {
        if (Path.GetExtension(file.Temp) == ".txt")
        {
            return EngineRunner.LaunchDerivedForTextAsync(file.Temp, file.Target, source.Temp, null);
        }

        return EngineRunner.LaunchDerivedAsync(file.Temp, file.Target, source.Temp, null);
    }

    static byte[] Whole(string extension) =>
        extension switch
        {
            ".txt" => Encoding.UTF8.GetBytes(received),
            ".pdf" => SamplePdf.Build("alpha", "bravo", "charlie"),
            ".docx" or ".xlsx" or ".pptx" => File.ReadAllBytes(Sample(extension)),
            ".svg" => Encoding.UTF8.GetBytes(SvgOf("red")),
            _ => MapOf(extension, moved: false)
        };

    static async Task Pair(DirectoryInfo directory, string name, string extension, byte[] received, byte[] verified)
    {
        await Assert.That(EngineTools.IsDetectedForExtension(EngineTool.DiffEngineViewer, extension)).IsTrue();
        var temp = Path.Combine(directory.FullName, $"{name}.received{extension}");
        var target = Path.Combine(directory.FullName, $"{name}.verified{extension}");
        await File.WriteAllBytesAsync(temp, received);
        await File.WriteAllBytesAsync(target, verified);
        await EngineRunner.LaunchAsync(EngineTool.DiffEngineViewer, temp, target);
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

        using var directory = new TempDirectory();
        var temp = Path.Combine(directory, $"Sample.received{extension}");
        var target = Path.Combine(directory, $"Sample.verified{extension}");
        await File.WriteAllBytesAsync(temp, received);
        await File.WriteAllBytesAsync(target, verified);

        ManualViewer.Expect(
            scenario,
            [$"Headers Sample.received{extension} and Sample.verified{extension}", .. checks]);

        await EngineRunner.LaunchAsync(EngineTool.DiffEngineViewer, temp, target);
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

    // Unnamed, so the queue labels each of these by its call site — which is what the expectations
    // of the inline cases tell the reader to look for. The cases that are about naming set one themselves.
    static engine::DiffEngine.InlinePatch Patch(
        string source,
        int line,
        string? expression,
        string content,
        EnginePatchMode mode = EnginePatchMode.Set,
        string? framework = null) =>
        new(source, line, expression, content, mode)
        {
            TestName = null,
            Framework = framework
        };

    /// <summary>
    /// Holds <paramref name="source"/> the way another process applying a patch to it would, by
    /// taking the cross process mutex InlineApplier waits on, so an accept-all stops when it gets
    /// there.
    /// <para>
    /// Taken only once a batch has started, so someone who accepts one at a time instead is never
    /// held up by it. Let go six seconds after the count stops moving, well inside the ten seconds
    /// InlineApplier waits before giving up, so the held entry still lands.
    /// </para>
    /// <para>
    /// A thread of its own, because a mutex has to be released by the thread that took it.
    /// </para>
    /// </summary>
    static Task HoldOnceReached(string source, int port, Cancel cancel) =>
        Task.Factory.StartNew(
            () =>
            {
                AcceptProgress? Progress() =>
                    ViewerClient.TrySend(new(ViewerVerb.List), out var response, port) ? response.Progress : null;

                while (Progress() is null)
                {
                    if (cancel.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(50)))
                    {
                        return;
                    }
                }

                using var mutex = new Mutex(false, PatchMutex(source));
                if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    return;
                }

                try
                {
                    // Stopped, rather than between two entries, once the count has stood still for
                    // longer than an apply takes
                    var previous = Progress();
                    var since = DateTime.UtcNow;
                    while (previous is not null &&
                           DateTime.UtcNow - since < TimeSpan.FromMilliseconds(300))
                    {
                        if (cancel.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(50)))
                        {
                            return;
                        }

                        var current = Progress();
                        if (current != previous)
                        {
                            previous = current;
                            since = DateTime.UtcNow;
                        }
                    }

                    // A batch that finished instead got past this class before it was taken
                    if (previous is not null)
                    {
                        cancel.WaitHandle.WaitOne(TimeSpan.FromSeconds(6));
                    }
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            },
            // Watched inside rather than handed over: a token here cancels the scheduling, and a
            // task that never ran would fault the await after the window closes
            Cancel.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    /// <summary>
    /// The name InlineApplier gives the mutex it waits on for a file. Worked out the same way here
    /// rather than shared, because nothing but this test needs it; if the two ever part, the
    /// batch simply does not stop, and the rest of the test still stands.
    /// </summary>
    static string PatchMutex(string source)
    {
        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(Path.GetFullPath(source).ToLowerInvariant()));
        return $"DiffEngineInline_{Convert.ToHexString(hash)}";
    }

    /// <summary>
    /// A class of <paramref name="methods"/> tests, each verifying and holding a snapshot, in the
    /// shape <see cref="WriteSource"/> writes one of. Returns the line of each verify call, which is
    /// what a patch reports.
    /// </summary>
    static (string Path, IReadOnlyList<int> Lines) WriteClass(DirectoryInfo directory, string name, int methods)
    {
        // Each piece ends with the blank line before its closing quotes, which is the line break
        // after its last line: the pieces are joined end to end.
        var builder = new StringBuilder(
            """
            public class Sample
            {

            """);
        var lines = new List<int>();
        for (var method = 1; method <= methods; method++)
        {
            // The class opens on two lines, each method takes seven, and the verify call is the
            // fourth of them
            lines.Add(2 + (method - 1) * 7 + 4);
            builder.Append(
                $$"""
                      [Test]
                      public Task Case{{method}}()
                      {
                          Verify(Build())
                              .Snapshot("old {{method}}");
                      }


                  """);
        }

        builder.Append(
            """
                static string Build() =>
                    "content";
            }

            """);
        return (Write(directory, name, builder.ToString()), lines);
    }

    /// <summary>
    /// Several lines, so accepting one writes a raw string literal and moves what follows it.
    /// </summary>
    static string Snapshot(int method) =>
        $$"""
          {
            Case: {{method}},
            Value: new
          }
          """;

    static int Unaccepted(string source)
    {
        var text = File.ReadAllText(source);
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(".Snapshot(\"old ", index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index++;
        }

        return count;
    }

    /// <summary>
    /// Line 6 is the verify call in both shapes, which is what every patch above points at.
    /// </summary>
    static string WriteSource(DirectoryInfo directory, string name, string? literal)
    {
        // Written with explicit indentation rather than a raw string, because the indentation is
        // the thing being tested: the patcher infers where to put a rewritten literal from it.
        var call = literal is null
            ? "        Verify(Build());"
            : $"        Verify(Build())\n            .Snapshot({literal});";

        return Write(
            directory,
            name,
            $$"""
              public class Sample
              {
                  [Test]
                  public Task Case()
                  {
              {{call}}
                  }

                  static string Build() =>
                      "content";
              }
              """);
    }

    static string Write(DirectoryInfo directory, string name, string content)
    {
        var path = Path.Combine(directory.FullName, name);
        File.WriteAllText(path, content);
        return path;
    }

    static string Long(bool changed)
    {
        var builder = new StringBuilder();
        for (var index = 1; index <= 40; index++)
        {
            if (index > 1)
            {
                builder.Append('\n');
            }

            builder.Append($"line {index:D2}");
            if (changed &&
                index is 3 or 17 or 33)
            {
                builder.Append(" changed");
            }
        }

        return builder.ToString();
    }

    static void Report(params string[] sources)
    {
        foreach (var source in sources)
        {
            Console.WriteLine();
            Console.WriteLine($"--- {Path.GetFileName(source)} after ---");
            Console.WriteLine(File.ReadAllText(source));
        }
    }
}
