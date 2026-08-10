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
        string content = Received) =>
        new(source, line, expression, content);

    /// <summary>
    /// Accept actions that report a fixed outcome, so the failure screens are reachable without
    /// arranging a locked file or a rewritten source.
    /// </summary>
    public static ViewerActions Applying(InlineApplyResult result) =>
        new(_ => result, static (_, _) => { });

    public static ViewerActions Applied =>
        Applying(InlineApplyResult.Applied);

    public static string Render(SessionState state) =>
        AsciiRenderer.Render(ScreenBuilder.Build(state));
}
