/// <summary>
/// A batch claims the snapshots of one source file together and has them written with one read and
/// one write. Applied one at a time each rewrote the whole file, and the rewrite is what costs:
/// five hundred snapshots in one file were half a minute where the drive is scanned. Each still
/// has an outcome of its own, and everything a single claim guards against is guarded for each.
/// </summary>
public class SameFileBatchTests
{
    [Test]
    public async Task TheSnapshotsOfOneFileAreWrittenTogether()
    {
        var together = new List<string>();
        var alone = new List<string>();
        var actions = Recording(together, alone, _ => InlineApplyResult.Applied);
        var state = ViewerSession.BeginAcceptAll(TwoFiles());
        var seen = new List<string>();

        while ((state = ViewerSession.ClaimNext(state)).Batch?.Current is not null)
        {
            seen.Add($"{ScreenBuilder.Build(state).Status}, {state.Queue.Count} pending");
            state = ViewerSession.ApplyClaimed(state, actions)(state);
        }

        await Assert.That(string.Join("\n", seen)).IsEqualTo(
            """
            Accepting 1 of 4, 4 pending
            Accepting 4 of 4, 1 pending
            """);
        await Assert.That(together).IsEquivalentTo(["SampleTests.cs:42 SampleTests.cs:88 SampleTests.cs:90"]);
        await Assert.That(alone).IsEquivalentTo(["OtherTests.cs:12"]);
        await Assert.That(state.Message).IsEqualTo("Accepted 4");
        await Assert.That(state.Queue).IsEmpty();
    }

    /// <summary>
    /// One write, and still an outcome each: the one that was written leaves, and the two that
    /// were not stay, each saying what the applier said of it.
    /// </summary>
    [Test]
    public async Task EachOfThemHasItsOwnOutcome()
    {
        var actions = Recording(
            [],
            [],
            _ => _.LineHint switch
            {
                88 => InlineApplyResult.Failed("the file is held"),
                90 => InlineApplyResult.NotFound("no Verify call"),
                _ => InlineApplyResult.Applied
            });

        var state = ViewerSession.Apply(TwoFiles(), CommandKind.AcceptAll, actions);

        await Assert.That(state.Queue.Select(_ => _.Name)).IsEquivalentTo(["SampleTests.cs:88", "SampleTests.cs:90"]);
        await Assert.That(state.Queue.Single(_ => _.Name == "SampleTests.cs:88").Status).IsEqualTo("the file is held");
        await Assert.That(state.Queue.Single(_ => _.Name == "SampleTests.cs:90").Status!).Contains("no Verify call");
        await Assert.That(state.Message).IsEqualTo("Accepted 2, 1 not written, 1 failed");
    }

    /// <summary>
    /// A re-run that replaces one of them while the file is being written is news about that one
    /// alone. It keeps the new content and is not counted, and the others are recorded as written.
    /// </summary>
    [Test]
    public async Task OneReplacedWhileTheyApplyKeepsItsNewContent()
    {
        var state = ViewerSession.ClaimNext(ViewerSession.BeginAcceptAll(TwoFiles()));
        var record = ViewerSession.ApplyClaimed(state, Fixtures.Applied);

        state = ViewerSession.EnqueueInline(state, Fixtures.Patch("SampleTests.cs", 88, "\"one\"", "third run"));
        state = record(state);

        await Assert.That(state.Queue.Select(_ => _.Name)).IsEquivalentTo(["SampleTests.cs:88", "OtherTests.cs:12"]);
        await Assert.That(state.Queue.Single(_ => _.Name == "SampleTests.cs:88").LeftText).IsEqualTo("third run");
        await Assert.That(state.Batch!.Tally.Accepted).IsEqualTo(2);
    }

    /// <summary>
    /// One that a second framework has made a conflict of is not taken with the others, as it is
    /// not claimed on its own: a bulk accept never picks a side.
    /// </summary>
    [Test]
    public async Task AConflictInTheFileIsNotClaimedWithTheRest()
    {
        var state = ViewerSession.EnqueueInline(
            Fixtures.Inline(
                Fixtures.Patch(framework: "net8.0"),
                Fixtures.Patch("SampleTests.cs", 88, "\"one\"", "two", framework: "net8.0"),
                Fixtures.Patch("SampleTests.cs", 90, "\"three\"", "four", framework: "net8.0")),
            Fixtures.Patch("SampleTests.cs", 88, "\"one\"", "nine", framework: "net9.0"));

        var claimed = ViewerSession.ClaimNext(ViewerSession.BeginAcceptAll(state));

        await Assert.That(claimed.Batch!.Current!.Name).IsEqualTo("SampleTests.cs:42");
        await Assert.That(claimed.Batch.Together.Select(_ => _.Name)).IsEquivalentTo(["SampleTests.cs:90"]);

        var done = ViewerSession.Apply(state, CommandKind.AcceptAll, Fixtures.Applied);
        await Assert.That(done.Message).IsEqualTo("Accepted 2, 1 conflict needs review");
    }

    /// <summary>
    /// A group's batch is some of the queue, so what it claims with a snapshot is the rest of that
    /// file within the group. A snapshot of the same file under another test is not the group's.
    /// </summary>
    [Test]
    public async Task AGroupTakesOnlyItsOwnSnapshotsOfTheFile()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch(testName: "First"),
            Fixtures.Patch("SampleTests.cs", 88, "\"one\"", "two", testName: "First"),
            Fixtures.Patch("SampleTests.cs", 90, "\"three\"", "four", testName: "Second"),
            Fixtures.Patch("SampleTests.cs", 95, "\"five\"", "six", testName: "Second"));
        var rows = QueueProjection.Visible(state, ScreenBuilder.BodyRows(state), out _).ToList();
        var open = ViewerSession.OpenMenu(state, rows.FindIndex(_ => _.GroupName == "First"));

        var claimed = ViewerSession.ClaimNext(ViewerSession.BeginAcceptGroup(open));

        await Assert.That(claimed.Batch!.Current!.Name).IsEqualTo("SampleTests.cs:42");
        await Assert.That(claimed.Batch.Together.Select(_ => _.Name)).IsEquivalentTo(["SampleTests.cs:88"]);

        var done = ViewerSession.Apply(open, CommandKind.AcceptGroup, Fixtures.Applied);
        await Assert.That(done.Queue.Select(_ => _.Name)).IsEquivalentTo(["SampleTests.cs:90", "SampleTests.cs:95"]);
        await Assert.That(done.Message).IsEqualTo("Accepted 2");
    }

    /// <summary>
    /// An applier that throws for a claim of several fails all of them, since which were written
    /// is not known, and the batch still finishes.
    /// </summary>
    [Test]
    public async Task AnApplierThatThrowsFailsEveryOneItWasHanded()
    {
        var host = new SessionHost(TwoFiles());
        var actions = Fixtures.Applied with
        {
            ApplyInlineTogether = _ => throw new("the disk went away")
        };
        host.Mutate(ViewerSession.BeginAcceptAll);

        var message = new AcceptAllRunner(host, actions).Drive();

        await Assert.That(message).IsEqualTo("Accepted 1, 3 failed");
        await Assert.That(host.State.Batch).IsNull();
        await Assert.That(host.State.Queue.Count).IsEqualTo(3);
        await Assert.That(host.State.Queue.All(_ => _.Status == "the disk went away")).IsTrue();
    }

    /// <summary>
    /// What a viewer applies with: several snapshots in one real file, one write.
    /// </summary>
    [Test]
    public async Task TheRealApplierWritesTheFileOnce()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"SameFileBatchTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "SampleTests.cs");
            await File.WriteAllTextAsync(
                source,
                """
                class C
                {
                    void One() => Verify(value).Snapshot("a");
                    void Two() => Verify(value).Snapshot("b");
                    void Three() => Verify(value).Snapshot("c");
                }
                """);
            var state = Fixtures.Inline(
                Fixtures.Patch(source, 3, "\"a\"", "one"),
                Fixtures.Patch(source, 4, "\"b\"", "two"),
                Fixtures.Patch(source, 5, "\"c\"", "three"));

            var done = ViewerSession.Apply(state, CommandKind.AcceptAll, ViewerActions.Real);

            await Assert.That(done.Message).IsEqualTo("Accepted 3");
            var written = await File.ReadAllTextAsync(source);
            await Assert.That(written).Contains("Snapshot(\"one\")");
            await Assert.That(written).Contains("Snapshot(\"two\")");
            await Assert.That(written).Contains("Snapshot(\"three\")");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // Three snapshots in one file and one in another.
    static SessionState TwoFiles() =>
        Fixtures.Inline(
            Fixtures.Patch(),
            Fixtures.Patch("SampleTests.cs", 88, "\"one\"", "two"),
            Fixtures.Patch("SampleTests.cs", 90, "\"three\"", "four"),
            Fixtures.Patch("OtherTests.cs", 12, null, "brand new"));

    static ViewerActions Recording(List<string> together, List<string> alone, Func<InlinePatch, InlineApplyResult> outcome) =>
        Fixtures.Applied with
        {
            ApplyInline = _ =>
            {
                alone.Add(Name(_));
                return outcome(_);
            },
            ApplyInlineTogether = _ =>
            {
                together.Add(string.Join(" ", _.Select(Name)));
                return _.Select(outcome).ToList();
            }
        };

    static string Name(InlinePatch patch) =>
        $"{Path.GetFileName(patch.SourceFile)}:{patch.LineHint}";
}
