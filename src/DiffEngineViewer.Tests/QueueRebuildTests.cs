/// <summary>
/// A change to the queue that is about one entry leaves every other entry where it is, as the
/// entry it was, rather than rebuilding the list from the whole queue. What it leaves has to be
/// what the rebuild would have: the same entries saying the same things, in the same order.
/// </summary>
public class QueueRebuildTests
{
    /// <summary>
    /// Random queues changed at random - arrivals at new call sites and old ones, from the same
    /// framework and another, call sites that moved, settles, accepts that land and that fail,
    /// discards, and files arriving between them - each checked against the list rebuilt whole
    /// from the same queue.
    /// </summary>
    [Test]
    public void EveryChangeLeavesWhatAWholeRebuildWould()
    {
        var random = new Random(41);
        for (var iteration = 0; iteration < 300; iteration++)
        {
            var state = SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows);
            var patches = new List<InlinePatch>();
            for (var step = 0; step < 60; step++)
            {
                var before = state;
                var pending = Pending(state);
                InlineQueue expected;
                string what;
                switch (random.Next(8))
                {
                    case 0 when patches.Count > 0:
                    {
                        // The same call site again: the same content, other content, or another
                        // framework's
                        var sent = patches[random.Next(patches.Count)];
                        var again = Copy(sent, sent.LineHint, $"content {random.Next(3)}", random.Next(3) == 0 ? "net9.0" : "net10.0");
                        what = $"again {again.SourceFile}:{again.LineHint}";
                        expected = pending.Enqueue(again);
                        state = ViewerSession.EnqueueInline(state, again);
                        break;
                    }
                    case 1 when patches.Count > 0:
                    {
                        // The same call site, reported at another line
                        var index = random.Next(patches.Count);
                        var moved = Copy(patches[index], patches[index].LineHint + 1000, patches[index].NewContent, "net10.0");
                        patches[index] = moved;
                        what = $"moved {moved.SourceFile}:{moved.LineHint}";
                        expected = pending.Enqueue(moved);
                        state = ViewerSession.EnqueueInline(state, moved);
                        break;
                    }
                    case 2 when patches.Count > 0:
                    {
                        var settling = patches[random.Next(patches.Count)];
                        var key = InlineKey.For(settling.SourceFile, settling.LineHint);
                        var origin = random.Next(2) == 0 ? "net10.0" : null;
                        what = $"settle {key} {origin}";
                        expected = pending.Settle(key, origin, settling.MemberName);
                        state = ViewerSession.Settle(state, key, origin, settling.MemberName);
                        break;
                    }
                    case 3 when state.Current is { Kind: QueueEntryKind.Inline } current:
                    {
                        var result = random.Next(3) == 0
                            ? InlineApplyResult.Failed("the file is held")
                            : InlineApplyResult.Applied;
                        what = $"accept {current.Key} {result.Status}";
                        expected = current.Conflicted
                            ? pending.Accept(current.Key, current.Variants[current.SelectedVariant].Origins[0], _ => result, out _)
                            : pending.Accept(current.Key, _ => result, out _);
                        state = ViewerSession.Apply(state, CommandKind.Accept, Fixtures.Applying(result));
                        break;
                    }
                    case 4 when state.Current is { Kind: QueueEntryKind.Inline } current:
                        what = $"discard {current.Key}";
                        expected = pending.Discard(current.Key, out _);
                        state = ViewerSession.Apply(state, CommandKind.Discard, Fixtures.Applied);
                        break;
                    case 5:
                        // A file, which goes in where it arrived and is put after its solution's
                        // snapshots by the next rebuild
                        state = ViewerSession.EnqueueTracked(
                            state,
                            Fixtures.Move($"Sample{random.Next(4)}.Test (txt)", Solution(random)));
                        continue;
                    case 6 when state.Queue.Count > 0:
                        state = ViewerSession.Apply(state, Command.Select(random.Next(state.Queue.Count)));
                        continue;
                    default:
                    {
                        var solution = Solution(random);
                        var file = $"Tests{random.Next(3)}.cs";
                        var arriving = Fixtures.Patch(
                            solution is null ? file : Fixtures.SolutionFile(solution, "Tests", file),
                            10 + step + iteration * 100,
                            $"\"old{step}\"",
                            $"content {random.Next(3)}",
                            testName: random.Next(2) == 0 ? $"Test{random.Next(3)}" : null,
                            framework: "net10.0");
                        arriving.MemberName = $"Member{random.Next(6)}";
                        patches.Add(arriving);
                        what = $"arrive {arriving.SourceFile}:{arriving.LineHint}";
                        expected = pending.Enqueue(arriving);
                        state = ViewerSession.EnqueueInline(state, arriving);
                        break;
                    }
                }

                var whole = ViewerSession.RebuildWhole(before, expected);
                if (Describe(whole) != Describe(state.Queue))
                {
                    Assert.Fail(
                        $"""
                         iteration {iteration} step {step}: {what}
                         before: {Describe(before.Queue)}
                         whole:  {Describe(whole)}
                         left:   {Describe(state.Queue)}
                         """);
                }

                // An entry the change did not touch is the entry it was, never one built again
                foreach (var entry in state.Queue)
                {
                    var was = before.Queue.FirstOrDefault(_ => _.Key == entry.Key);
                    if (was is not null &&
                        ReferenceEquals(was.Variants, entry.Variants) &&
                        was.Status == entry.Status &&
                        !ReferenceEquals(was, entry))
                    {
                        Assert.Fail($"iteration {iteration} step {step}: {what} built {entry.Key} again");
                    }
                }
            }
        }
    }

    /// <summary>
    /// What the short way is for, seen by what a test failing again allocates in a long queue: the
    /// list is copied, and nothing is made for the entries the arrival was not about.
    /// </summary>
    [Test]
    public async Task ARerunInALongQueueIsAboutOneEntry()
    {
        var state = SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows);
        for (var index = 0; index < 2000; index++)
        {
            state = ViewerSession.EnqueueInline(
                state,
                Fixtures.Patch($"Sample{index % 40}Tests.cs", 10 + index, "\"old\"", $"new {index}", testName: $"Test{index}"));
        }

        var arriving = Fixtures.Patch("Sample20Tests.cs", 510, "\"old\"", "something else", testName: "Test500");
        var before = GC.GetAllocatedBytesForCurrentThread();
        var arrived = ViewerSession.EnqueueInline(state, arriving);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(arrived.Queue.Count).IsEqualTo(2000);
        await Assert.That(arrived.Queue.Single(_ => _.TestName == "Test500").LeftText).IsEqualTo("something else");
        // Rebuilt whole it was nearly two megabytes. What is left is the queue the session asks
        // InlineQueue of, an item an entry, and the list
        await Assert.That(allocated).IsLessThan(700_000);
    }

    static InlinePatch Copy(InlinePatch patch, int line, string content, string framework) =>
        new(patch.SourceFile, line, patch.OriginalExpression, content)
        {
            TestName = patch.TestName,
            MemberName = patch.MemberName,
            Framework = framework
        };

    static string? Solution(Random random) =>
        random.Next(3) switch
        {
            0 => "SolutionA",
            1 => "SolutionB",
            _ => null
        };

    static InlineQueue Pending(SessionState state) =>
        InlineQueue.From(
            state.Queue
                .Where(_ => _.Kind == QueueEntryKind.Inline)
                .Select(_ => new PendingInline(_.Variants, _.Status)));

    // Everything a rebuild decides about an entry: where it is, what it holds and from which
    // frameworks, which of those is on screen, and what the last attempt said
    static string Describe(IReadOnlyList<QueueEntry> queue) =>
        string.Join(
            " | ",
            queue.Select(_ =>
                $"{_.Kind} {_.Key} [{string.Join(";", _.Variants.Select(variant => $"{variant.Patch.NewContent}@{variant.Patch.LineHint}<{string.Join(",", variant.Origins)}>"))}] v{_.SelectedVariant} {_.Status} '{_.LeftText}'"));
}
