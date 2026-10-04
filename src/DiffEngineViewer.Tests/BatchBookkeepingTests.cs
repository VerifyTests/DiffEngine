/// <summary>
/// What a batch does to the queue besides applying it. Recording an entry takes it out of the list
/// or marks it where it stands, and finding what is visible walks the queue without describing
/// it: each used to rebuild or describe the whole queue an entry, which for 2,000 snapshots was
/// seconds and gigabytes beside the applying. Both have to come to what the long way round gave.
/// </summary>
public class BatchBookkeepingTests
{
    /// <summary>
    /// Every other inline transition rebuilds the list from the queue and orders it. A batch's
    /// record does neither, so after each entry it records the list has to be in the order a
    /// rebuild would have left it in, whatever became of the entry: written, refused, or failed.
    /// </summary>
    [Test]
    public void AfterEachEntryTheQueueIsStillInOrder()
    {
        var random = new Random(11);
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var state = ViewerSession.BeginAcceptAll(Mixed(random));
            var actions = Fixtures.Applied with
            {
                ApplyInline = _ => random.Next(4) switch
                {
                    0 => InlineApplyResult.Failed("the file is held"),
                    1 => InlineApplyResult.NotFound("no Verify call"),
                    _ => InlineApplyResult.Applied
                },
                MoveFile = static (_, _) =>
                {
                },
                DeleteFile = static _ =>
                {
                }
            };
            while ((state = ViewerSession.ClaimNext(state)).Batch?.Current is { } entry)
            {
                state = ViewerSession.ApplyClaimed(state, actions)(state);
                if (!QueueProjection.Order(state.Queue).SequenceEqual(state.Queue, ReferenceEqualityComparer.Instance))
                {
                    Assert.Fail($"Out of order after {entry.Name}: {string.Join(", ", state.Queue.Select(_ => _.Name))}");
                }
            }
        }
    }

    /// <summary>
    /// And what it leaves is what the batch's own rules say of each entry: one that was written is
    /// gone, one that was not is still there saying why, and nothing else was touched.
    /// </summary>
    [Test]
    public async Task EachEntryIsLeftAsTheBatchRulesSay()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch("OneTests.cs", 10),
            Fixtures.Patch("TwoTests.cs", 20, "\"a\"", "b"),
            Fixtures.Patch("ThreeTests.cs", 30, "\"c\"", "d"),
            Fixtures.Patch("FourTests.cs", 40, "\"e\"", "f"));
        var untouched = state.Queue.Single(_ => _.Name == "FourTests.cs:40");
        var actions = Fixtures.Applied with
        {
            ApplyInline = _ => _.LineHint switch
            {
                20 => InlineApplyResult.Failed("the file is held"),
                30 => InlineApplyResult.NotFound("no Verify call"),
                _ => InlineApplyResult.Applied
            }
        };

        state = ViewerSession.BeginAcceptAll(state);
        while ((state = ViewerSession.ClaimNext(state)).Batch?.Current is { } entry &&
               entry.Name != "FourTests.cs:40")
        {
            state = ViewerSession.ApplyClaimed(state, actions)(state);
        }

        await Assert.That(state.Queue.Select(_ => _.Name)).IsEquivalentTo(["TwoTests.cs:20", "ThreeTests.cs:30", "FourTests.cs:40"]);
        await Assert.That(state.Queue.Single(_ => _.Name == "TwoTests.cs:20").Status).IsEqualTo("the file is held");
        await Assert.That(state.Queue.Single(_ => _.Name == "ThreeTests.cs:30").Status!).Contains("no Verify call");
        // The very entry, not one rebuilt to look like it: nothing about it changed
        await Assert.That(state.Queue.Single(_ => _.Name == "FourTests.cs:40")).IsSameReferenceAs(untouched);
        await Assert.That(state.Batch!.Tally).IsEqualTo(new(1, 1, 1, state.Batch.Tally.Failure));
    }

    /// <summary>
    /// Which entries have a row is asked without describing the rows, on every step through the
    /// queue and after every entry a batch takes out. It is the same walk, so it has to give what
    /// the rows that are drawn give, with nothing folded and with each header folded in turn.
    /// </summary>
    [Test]
    public async Task WhatIsVisibleIsWhatHasARow()
    {
        var random = new Random(12);
        for (var iteration = 0; iteration < 50; iteration++)
        {
            var state = Mixed(random);
            await SameAsTheRows(state);
            foreach (var header in QueueProjection.Rows(state).Where(_ => _.GroupKey is not null).ToList())
            {
                await SameAsTheRows(ViewerSession.ToggleGroup(state, header.GroupKey!));
            }
        }
    }

    static async Task SameAsTheRows(SessionState state)
    {
        var drawn = QueueProjection.Rows(state)
            .Where(_ => _.EntryIndex >= 0)
            .Select(_ => _.EntryIndex);

        await Assert.That(string.Join(",", QueueProjection.VisibleEntries(state))).IsEqualTo(string.Join(",", drawn));
    }

    /// <summary>
    /// Snapshots across two solutions and outside any, some of them several to a test, so there
    /// are solution headers and test headers to order under, with a move and a delete beside them.
    /// </summary>
    static SessionState Mixed(Random random)
    {
        string[] solutions = ["SolutionA", "SolutionB"];
        var patches = new List<InlinePatch>();
        for (var index = random.Next(3, 12); index > 0; index--)
        {
            var file = $"Tests{random.Next(3)}.cs";
            var source = random.Next(3) == 0
                ? file
                : Fixtures.SolutionFile(solutions[random.Next(2)], "Tests", file);
            patches.Add(
                Fixtures.Patch(
                    source,
                    10 + patches.Count,
                    $"\"old{patches.Count}\"",
                    $"new {patches.Count}",
                    testName: random.Next(2) == 0 ? $"Test{random.Next(2)}" : null));
        }

        var state = Fixtures.Inline([.. patches]);
        state = ViewerSession.EnqueueTracked(state, Fixtures.Move(solution: "SolutionA"));
        return ViewerSession.EnqueueTracked(state, Fixtures.Delete(solution: "SolutionB"));
    }
}
