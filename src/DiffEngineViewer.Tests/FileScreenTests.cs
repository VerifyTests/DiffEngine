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

    [Test]
    public Task NextChange()
    {
        var state = Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));
        return Verify(Fixtures.Render(Apply(state, CommandKind.NextChange, CommandKind.NextChange)));
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
