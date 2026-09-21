/// <summary>
/// The minimal view: only the changes, each with three rows either side, and every longer run of
/// unchanged rows folded into one row. <see cref="Fixtures.Long"/> changes rows 2, 16 and 32 of 40,
/// which folds to 23 rows: lines 1-6, a fold of 7, lines 14-20, a fold of 9, lines 30-36 and a fold
/// of 4.
/// </summary>
public class MinimalViewTests
{
    [Test]
    public async Task Folds_every_unchanged_run_longer_than_one_row()
    {
        var view = Minimal(Long()).View!;

        await Assert.That(view.Count).IsEqualTo(23);
        await Assert.That(Folds(view)).IsEqualTo(
            "... 7 unchanged lines ... | ... 9 unchanged lines ... | ... 4 unchanged lines ...");
    }

    /// <summary>
    /// One unchanged row between two changes' context would fold into a row of its own, taking the
    /// same space as the line and saying less, so it is shown. Nothing else folds here either, and
    /// then the minimal view is the full one rather than a copy of it.
    /// </summary>
    [Test]
    public async Task A_single_row_between_two_contexts_is_shown()
    {
        var state = Fixtures.File(Lines(12, 1, 9), Lines(12));

        var entry = state.Current!;

        await Assert.That(entry.View(true)).IsSameReferenceAs(entry.View(false));
    }

    [Test]
    public async Task Everything_folds_when_nothing_changed()
    {
        var view = Minimal(Fixtures.File(Fixtures.Long(false), Fixtures.Long(false))).View!;

        await Assert.That(view.Count).IsEqualTo(1);
        await Assert.That(Folds(view)).IsEqualTo("... 40 unchanged lines ...");
    }

    /// <summary>
    /// A picture's rows are its properties, each worth reading, so there is nothing to fold and the
    /// button that would is disabled rather than doing nothing.
    /// </summary>
    [Test]
    public async Task A_picture_is_never_folded()
    {
        var state = Fixtures.Images();

        var entry = state.Current!;

        await Assert.That(entry.View(true)).IsSameReferenceAs(entry.View(false));
        await Assert.That(Button(state, CommandKind.ToggleMinimal).Enabled).IsFalse();
    }

    /// <summary>
    /// Switching views keeps the reader's place: the change on screen stays on the same line of
    /// the screen while what is around it folds away, and comes back to where it was.
    /// </summary>
    [Test]
    public async Task Switching_keeps_the_change_being_read_where_it_is_on_screen()
    {
        var full = ViewerSession.Apply(Long(), CommandKind.NextChange);
        await Assert.That(ScreenLineOf(full, 17)).IsEqualTo(3);

        var minimal = ViewerSession.Apply(full, CommandKind.ToggleMinimal);
        var back = ViewerSession.Apply(minimal, CommandKind.ToggleMinimal);

        await Assert.That(ScreenLineOf(minimal, 17)).IsEqualTo(3);
        await Assert.That(back.Minimal).IsFalse();
        await Assert.That(back.ScrollTop).IsEqualTo(full.ScrollTop);
    }

    /// <summary>
    /// Nothing on screen survives folding when the reader was inside a run the minimal view folds,
    /// so the fold standing for where they were is what goes at the top.
    /// </summary>
    [Test]
    public async Task Folding_from_inside_a_folded_run_puts_that_fold_at_the_top()
    {
        var state = ViewerSession.Apply(
            Fixtures.File(Fixtures.Deep(true), Fixtures.Deep(false)),
            Command.Scroll(5));

        var minimal = Minimal(state);

        await Assert.That(minimal.ScrollTop).IsEqualTo(0);
        await Assert.That(ScreenBuilder.Build(minimal).Left.Rows[0].Kind).IsEqualTo(RowKind.Folded);
    }

    /// <summary>
    /// Navigation counts rows of the view on screen: the second change is row 10 of the minimal
    /// view, brought in from row 7 with the three rows of its context above it, which is also the
    /// last page of 23 rows in 16.
    /// </summary>
    [Test]
    public async Task Change_navigation_counts_rows_of_the_minimal_view()
    {
        var minimal = Minimal(Long());

        var next = ViewerSession.Apply(minimal, CommandKind.NextChange);
        var past = ViewerSession.Apply(next, CommandKind.NextChange);
        var back = ViewerSession.Apply(past, CommandKind.PreviousChange);

        await Assert.That(next.ScrollTop).IsEqualTo(7);
        await Assert.That(ScreenLineOf(next, 17)).IsEqualTo(3);
        await Assert.That(past.ScrollTop).IsEqualTo(7);
        await Assert.That(back.ScrollTop).IsEqualTo(0);
    }

    /// <summary>
    /// The status line names the stretch of the file on screen, folds and all, rather than a
    /// position in the list of rows the view happens to have: sixteen rows here run from line 1 to
    /// line 30 of 40.
    /// </summary>
    [Test]
    public async Task The_status_line_counts_lines_of_the_file()
    {
        var minimal = Minimal(Long());

        await Assert.That(ScreenBuilder.Build(minimal).Status).IsEqualTo("lines 1-30 of 40");
    }

    /// <summary>
    /// A view setting rather than something one entry has, so it holds for whatever is selected
    /// next.
    /// </summary>
    [Test]
    public async Task The_view_holds_across_entries()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch("A.cs", 1, Fixtures.Literal(Fixtures.Long(false)), Fixtures.Long(true)),
            Fixtures.Patch("B.cs", 2, Fixtures.Literal(Fixtures.Long(false)), Fixtures.Long(true)));

        var stepped = ViewerSession.Apply(Minimal(state), CommandKind.NextItem);

        await Assert.That(stepped.Minimal).IsTrue();
        await Assert.That(stepped.Current!.Name).IsEqualTo("B.cs:2");
        await Assert.That(stepped.View!.Count).IsEqualTo(23);
    }

    [Test]
    public async Task The_fold_button_says_what_it_switches_to()
    {
        var state = Long();

        await Assert.That(Button(state, CommandKind.ToggleMinimal).Label).IsEqualTo("Changes only");
        await Assert.That(Button(Minimal(state), CommandKind.ToggleMinimal).Label).IsEqualTo("All lines");
    }

    /// <summary>
    /// The change buttons say whether there is a change left to go to, which is otherwise only
    /// found out by scrolling.
    /// </summary>
    [Test]
    public async Task The_change_buttons_are_enabled_only_with_somewhere_to_go()
    {
        var first = Long();
        var second = ViewerSession.Apply(first, CommandKind.NextChange);
        var last = ViewerSession.Apply(second, CommandKind.NextChange);

        await Assert.That(Enabled(first)).IsEqualTo("previous: False, next: True");
        await Assert.That(Enabled(second)).IsEqualTo("previous: True, next: True");
        await Assert.That(Enabled(last)).IsEqualTo("previous: True, next: False");
    }

    /// <summary>
    /// What is on screen is this window's to lay out however it likes, so switching is never
    /// forwarded to the queue's owner the way accepting is.
    /// </summary>
    [Test]
    public async Task Switching_is_local_when_displaying_someone_elses_queue()
    {
        var state = Fixtures.Attached(InlineQueue.Empty, Fixtures.Move());
        var link = new OwnerLink(new(state), port: 1);

        var switched = ViewerProgram.Apply(
            state,
            new(CommandKind.ToggleMinimal, -1, -1, 0, false, Fixtures.Columns, Fixtures.Rows),
            link,
            new NoWindow());

        await Assert.That(switched.Minimal).IsTrue();
        await Assert.That(switched.Message).IsNull();
    }

    static SessionState Long() =>
        Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));

    static SessionState Minimal(SessionState state) =>
        ViewerSession.Apply(state, CommandKind.ToggleMinimal);

    /// <summary>
    /// A text of numbered lines with some of them changed.
    /// </summary>
    static string Lines(int count, params int[] changed) =>
        string.Join(
            "\n",
            Enumerable
                .Range(1, count)
                .Select(_ => changed.Contains(_) ? $"line {_:D2} changed" : $"line {_:D2}"));

    static string Folds(DiffView view) =>
        string.Join(
            " | ",
            view.Left
                .Where(_ => _.Kind == RowKind.Folded)
                .Select(_ => _.Text));

    static int ScreenLineOf(SessionState state, int line)
    {
        var rows = ScreenBuilder.Build(state).Left.Rows;
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].LineNumber == line)
            {
                return index;
            }
        }

        throw new($"Line {line} is not on screen.");
    }

    static Button Button(SessionState state, CommandKind command) =>
        ScreenBuilder.Build(state).Buttons.Single(_ => _.Command == command);

    static string Enabled(SessionState state) =>
        $"previous: {Button(state, CommandKind.PreviousChange).Enabled}, next: {Button(state, CommandKind.NextChange).Enabled}";

    sealed class NoWindow : IViewerWindow
    {
        public bool Present(Screen screen) =>
            true;

        public ViewerInput Poll() =>
            default;

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
