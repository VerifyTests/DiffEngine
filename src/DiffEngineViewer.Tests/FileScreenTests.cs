public class FileScreenTests
{
    [Test]
    public Task Initial() =>
        Verify(Fixtures.Render(Fixtures.File()));

    [Test]
    public Task NoDifferences() =>
        Verify(Fixtures.Render(Fixtures.File(Fixtures.Expected)));

    [Test]
    public Task LeftEmpty() =>
        Verify(Fixtures.Render(Fixtures.File(left: "")));

    [Test]
    public Task RightEmpty() =>
        Verify(Fixtures.Render(Fixtures.File(right: "")));

    /// <summary>
    /// A snapshot that differs only in whitespace still has to render as a difference. DiffPlex
    /// ignores whitespace by default, which turned this into three Unchanged rows.
    /// </summary>
    [Test]
    public Task WhitespaceOnly() =>
        Verify(
            Fixtures.Render(
                Fixtures.File(
                    "the quick\n  brown fox\ndog ",
                    "the quick\nbrown fox\ndog")));

    [Test]
    public Task LongLines()
    {
        var line = new string('x', 400);
        return Verify(Fixtures.Render(Fixtures.File($"start\n{line}\nend", $"start\n{line}!\nend")));
    }

    [Test]
    public Task Scrolled()
    {
        var state = Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));
        return Verify(Fixtures.Render(Apply(state, CommandKind.PageDown)));
    }

    [Test]
    public Task AtEnd()
    {
        var state = Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));
        return Verify(Fixtures.Render(Apply(state, CommandKind.ScrollEnd)));
    }

    /// <summary>
    /// The second change, with the three rows leading into it above it.
    /// </summary>
    [Test]
    public Task NextChange()
    {
        var state = Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));
        return Verify(Fixtures.Render(Apply(state, CommandKind.NextChange)));
    }

    /// <summary>
    /// A comparison whose first change is far down the file opens on that change rather than on
    /// line 1, which is the part of the file nothing is wrong with.
    /// </summary>
    [Test]
    public Task OpensAtTheFirstChange() =>
        Verify(Fixtures.Render(Fixtures.File(Fixtures.Deep(true), Fixtures.Deep(false))));

    /// <summary>
    /// Only the changes and the three rows either side of each, with every longer run of unchanged
    /// rows folded into one row saying how many it stands for.
    /// </summary>
    [Test]
    public Task Minimal()
    {
        var state = Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));
        return Verify(Fixtures.Render(Apply(state, CommandKind.ToggleMinimal)));
    }

    [Test]
    public Task MinimalAtEnd()
    {
        var state = Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));
        return Verify(Fixtures.Render(Apply(state, CommandKind.ToggleMinimal, CommandKind.ScrollEnd)));
    }

    /// <summary>
    /// Nothing differs, so everything folds: one row saying so rather than an empty pane, which
    /// would read as a comparison with nothing in it.
    /// </summary>
    [Test]
    public Task MinimalNoDifferences()
    {
        var state = Fixtures.File(Fixtures.Long(false), Fixtures.Long(false));
        return Verify(Fixtures.Render(Apply(state, CommandKind.ToggleMinimal)));
    }

    static SessionState Apply(SessionState state, params CommandKind[] commands)
    {
        foreach (var command in commands)
        {
            state = ViewerSession.Apply(state, command);
        }

        return state;
    }
}
