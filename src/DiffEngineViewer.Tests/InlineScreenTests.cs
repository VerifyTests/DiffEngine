public class InlineScreenTests
{
    [Test]
    public Task Single() =>
        Verify(Fixtures.Render(Fixtures.Inline(Fixtures.Patch())));

    [Test]
    public Task EmptyQueue() =>
        Verify(Fixtures.Render(Fixtures.Inline()));

    [Test]
    public Task NewSnapshot() =>
        Verify(Fixtures.Render(Fixtures.Inline(Fixtures.Patch(expression: null))));

    [Test]
    public Task UnparsedExpression() =>
        Verify(Fixtures.Render(Fixtures.Inline(Fixtures.Patch(expression: "$\"the {value} fox\""))));

    [Test]
    public Task Queue() =>
        Verify(Fixtures.Render(Pending()));

    [Test]
    public Task SecondItemSelected() =>
        Verify(Fixtures.Render(ViewerSession.Apply(Pending(), CommandKind.NextItem, Fixtures.Applied)));

    [Test]
    public Task AfterAccept() =>
        Verify(Fixtures.Render(ViewerSession.Apply(Pending(), CommandKind.Accept, Fixtures.Applied)));

    [Test]
    public Task AfterDiscard() =>
        Verify(Fixtures.Render(ViewerSession.Apply(Pending(), CommandKind.Discard, Fixtures.Applied)));

    [Test]
    public Task AfterAcceptAll() =>
        Verify(Fixtures.Render(ViewerSession.Apply(Pending(), CommandKind.AcceptAll, Fixtures.Applied)));

    [Test]
    public Task StalePatch()
    {
        var actions = Fixtures.Applying(InlineApplyResult.NotFound("Could not locate the VerifyInline call"));
        return Verify(Fixtures.Render(ViewerSession.Apply(Pending(), CommandKind.Accept, actions)));
    }

    [Test]
    public Task FailedApply()
    {
        var actions = Fixtures.Applying(InlineApplyResult.Failed("Failed to write: SampleTests.cs"));
        return Verify(Fixtures.Render(ViewerSession.Apply(Pending(), CommandKind.Accept, actions)));
    }

    [Test]
    public Task FailedApplyThenAcceptAll()
    {
        var actions = Fixtures.Applying(InlineApplyResult.Failed("Failed to write: SampleTests.cs"));
        var state = ViewerSession.Apply(Pending(), CommandKind.AcceptAll, actions);
        return Verify(Fixtures.Render(state));
    }

    static SessionState Pending() =>
        Fixtures.Inline(
            Fixtures.Patch(),
            Fixtures.Patch("SampleTests.cs", 88, "\"one\"", "two"),
            Fixtures.Patch("OtherTests.cs", 12, null, "brand new"),
            Fixtures.Patch("OtherTests.cs", 30, "\"a\"", "b"),
            Fixtures.Patch("WideNamedTests.cs", 501, "\"x\"", "y"));
}
