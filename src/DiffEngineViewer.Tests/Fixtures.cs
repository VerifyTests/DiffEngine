static class Fixtures
{
    /// <summary>
    /// Fixed so every screen snapshot has the same grid. 24 rows leaves 16 body rows.
    /// </summary>
    public const int Columns = 96;

    public const int Rows = 24;

    public const string Received =
        """
        the quick
        brown dog
        jumps over
        the lazy
        dog
        """;

    public const string Expected =
        """
        the quick
        brown fox
        jumps over
        the lazy
        dog
        """;

    /// <summary>
    /// Forty lines with changes at 3, 17 and 33, so scrolling and next/previous change have
    /// something to land on both inside and outside the first viewport.
    /// </summary>
    public static string Long(bool changed)
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

    public static SessionState File(string left = Received, string right = Expected) =>
        ViewerSession.EnqueueFile(
            SessionState.Start(ViewerMode.File, Columns, Rows),
            QueueEntry.ForFiles("Sample.received.txt", "Sample.verified.txt", left, right));

    public static SessionState Inline(params InlinePatch[] patches)
    {
        var state = SessionState.Start(ViewerMode.Inline, Columns, Rows);
        foreach (var patch in patches)
        {
            state = ViewerSession.EnqueueInline(state, patch);
        }

        return state;
    }

    public static InlinePatch Patch(
        string source = "SampleTests.cs",
        int line = 42,
        string? expression = "\"\"\"\n    the quick\n    brown fox\n    jumps over\n    the lazy\n    dog\n    \"\"\"",
        string content = Received,
        string? testName = null,
        string? framework = null) =>
        new(source, line, expression, content)
        {
            TestName = testName,
            Framework = framework
        };

    /// <summary>
    /// A source path under a real solution marker directory in temp, so grouping exercises the
    /// real finder while only the solution name reaches a snapshot. The directory persists across
    /// runs deliberately: the finder caches per path, and the marker being already there changes
    /// nothing it answers.
    /// </summary>
    public static string SolutionFile(string solution, string project, string file)
    {
        var directory = Path.Combine(Path.GetTempPath(), "deview-fixtures", solution);
        Directory.CreateDirectory(directory);
        var marker = Path.Combine(directory, $"{solution}.slnx");
        if (!System.IO.File.Exists(marker))
        {
            try
            {
                System.IO.File.WriteAllText(marker, "");
            }
            catch (IOException)
            {
                // Tests run in parallel and two can create the same marker; whoever lost still
                // finds it there.
            }
        }

        var projectDirectory = Path.Combine(directory, project);
        Directory.CreateDirectory(projectDirectory);
        return Path.Combine(projectDirectory, file);
    }

    /// <summary>
    /// A move entry built from in-memory text, the way <see cref="Applying"/> keeps accepting off
    /// disk.
    /// </summary>
    // Forward slashes, deliberately: GetFileName splits on them on every platform, while a
    // Windows-style path renders whole on Linux and macOS and fails their snapshot runs.
    public static QueueEntry Move(
        string name = "Sample.Test (txt)",
        string? solution = null,
        string left = Received,
        string right = Expected) =>
        QueueEntry.ForMove(
            $"move:temp/{name}",
            name,
            solution,
            "temp/sample.received.txt",
            "code/sample.verified.txt",
            new(left, null, null),
            new(right, null, null));

    public static QueueEntry Delete(
        string name = "extra.verified.txt",
        string? solution = null,
        string content = Expected) =>
        QueueEntry.ForDelete(
            $"delete:code/{name}",
            name,
            solution,
            $"code/{name}",
            new(content, null, null));

    /// <summary>
    /// A state displaying someone else's queue, without a socket: what an attached viewer holds
    /// after one pump.
    /// </summary>
    public static SessionState Attached(InlineQueue pending, params QueueEntry[] changes) =>
        ViewerSession.Sync(SessionState.Start(ViewerMode.Inline, Columns, Rows), pending, changes, null);

    public static InlineQueue Pending(params InlinePatch[] patches)
    {
        var queue = InlineQueue.Empty;
        foreach (var patch in patches)
        {
            queue = queue.Enqueue(patch);
        }

        return queue;
    }

    /// <summary>
    /// Accept actions that report a fixed outcome, so the failure screens are reachable without
    /// arranging a locked file or a rewritten source.
    /// </summary>
    public static ViewerActions Applying(InlineApplyResult result) =>
        new(_ => result, static (_, _) =>
        {
        }, static _ =>
        {
        });

    public static ViewerActions Applied =>
        Applying(InlineApplyResult.Applied);

    /// <summary>
    /// File mode's half of the same idea: a copy that records or throws, so accepting a comparison
    /// is reachable without touching disk.
    /// </summary>
    public static ViewerActions Copying(Action<string, string> copy) =>
        new(static _ => throw new("File mode does not apply patches."), copy, static _ =>
        {
        });

    public static string Render(SessionState state) =>
        AsciiRenderer.Render(ScreenBuilder.Build(state));

    /// <summary>
    /// The one grouped-and-conflicted scene the pixel suites mirror: two solutions, a test
    /// sub-group, and the selection on a conflicted entry so the variant button is on screen.
    /// Shared here so the heads capture the same frame the ASCII snapshots describe.
    /// </summary>
    public static SessionState GroupedConflicted()
    {
        var state = Inline(
            Patch(SolutionFile("SolutionA", "Tests", "ATests.cs"), 10, testName: "Compare handles nulls"),
            Patch(SolutionFile("SolutionA", "Tests", "ATests.cs"), 30, "\"a\"", "b", testName: "Compare handles nulls"),
            Patch(SolutionFile("SolutionB", "Tests", "BTests.cs"), 42, content: "eight", framework: "net8.0"),
            Patch(SolutionFile("SolutionB", "Tests", "BTests.cs"), 42, content: "nine", framework: "net9.0"));
        state = ViewerSession.Apply(state, CommandKind.NextItem);
        return ViewerSession.Apply(state, CommandKind.NextItem);
    }
}
