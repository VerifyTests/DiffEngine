/// <summary>
/// A pending move and a pending delete that name the same verified file, in a queue this viewer
/// owns. The delete can be the last copy of a snapshot leaving and the move is the snapshot
/// arriving, so a bulk accept that carries out both, the move first, leaves neither file. The
/// tray's tracker has the same pair and the same rules for it (TrackerMoveOntoDeleteTest).
/// </summary>
public class MoveOntoDeleteTests
{
    const string temp = "temp/sample.received.txt";
    const string target = "code/sample.verified.txt";

    /// <summary>
    /// The order a batch took them in when the delete was raised second: the received file was
    /// moved into place and then deleted.
    /// </summary>
    [Test]
    public async Task An_accept_all_keeps_the_file_its_move_just_wrote()
    {
        var disk = new Disk();
        var state = Queued(Fixtures.Move(), Delete());

        var swept = ViewerSession.Apply(state, CommandKind.AcceptAll, disk.Actions);

        await Assert.That(disk.Files[target]).IsEqualTo("received");
        var delete = swept.Queue.Single();
        await Assert.That(delete.Kind).IsEqualTo(QueueEntryKind.Delete);
        await Assert.That(delete.Status).IsEqualTo(ViewerSession.WroteItsFile);
        await Assert.That(swept.Message).IsEqualTo($"Accepted 0, plus 1 files (1 kept). {ViewerSession.DeletesKept}");
    }

    /// <summary>
    /// A move onto a file is a run that verified against it, so the delete an earlier run raised
    /// for that file no longer describes a stale one, and goes as the move arrives.
    /// </summary>
    [Test]
    public async Task A_move_withdraws_the_delete_pending_on_its_target()
    {
        var disk = new Disk();
        var state = Queued(Delete(), Fixtures.Move());

        await Assert.That(state.Queue.Select(_ => _.Kind)).IsEquivalentTo([QueueEntryKind.Move]);
        await Assert.That(state.Current!.Kind).IsEqualTo(QueueEntryKind.Move);

        var swept = ViewerSession.Apply(state, CommandKind.AcceptAll, disk.Actions);

        await Assert.That(disk.Files[target]).IsEqualTo("received");
        await Assert.That(swept.Queue).IsEmpty();
    }

    /// <summary>
    /// The delete of some other file is nothing to do with the move, and goes with it.
    /// </summary>
    [Test]
    public async Task A_delete_of_another_file_is_carried_out()
    {
        var disk = new Disk();
        disk.Files["code/extra.verified.txt"] = "stale";
        var state = Queued(Fixtures.Delete(), Fixtures.Move(), Fixtures.Delete("other.verified.txt"));

        var swept = ViewerSession.Apply(state, CommandKind.AcceptAll, disk.Actions);

        await Assert.That(swept.Queue).IsEmpty();
        await Assert.That(disk.Files.Keys).IsEquivalentTo([target]);
        await Assert.That(swept.Message).IsEqualTo("Accepted 0, plus 3 files");
    }

    /// <summary>
    /// The hold is the delete's, not the batch's: a second accept-all, or one after the move was
    /// accepted on its own, finds a delete that looks like any other unless it remembers.
    /// </summary>
    [Test]
    public async Task A_delete_whose_file_a_single_accept_wrote_is_held_by_every_accept_all_after()
    {
        var disk = new Disk();
        var state = Queued(Fixtures.Move(), Delete());
        state = ViewerSession.SelectKey(state, Fixtures.Move().Key);

        state = ViewerSession.Apply(state, CommandKind.Accept, disk.Actions);
        await Assert.That(state.Queue.Single().Status).IsEqualTo(ViewerSession.WroteItsFile);

        state = ViewerSession.Apply(state, CommandKind.AcceptAll, disk.Actions);
        state = ViewerSession.Apply(state, CommandKind.AcceptAll, disk.Actions);

        await Assert.That(disk.Files[target]).IsEqualTo("received");
        await Assert.That(state.Queue.Single().Kind).IsEqualTo(QueueEntryKind.Delete);
    }

    /// <summary>
    /// A held delete's reason is its status, and every head draws a row with a status as an entry
    /// that failed. Nothing failed, so the row leads with a mark of its own and carries no
    /// status; why it is held stays in its tip. A delete that was tried and could not be deleted
    /// is still the failure it was.
    /// </summary>
    [Test]
    public async Task A_held_delete_is_marked_apart_from_a_failed_one()
    {
        var disk = new Disk();
        var state = Queued(Fixtures.Move(), Delete());
        state = ViewerSession.Apply(state, CommandKind.AcceptAll, disk.Actions);

        var held = QueueProjection.Rows(state).Single();
        await Assert.That(held.Label).IsEqualTo("~ sample.verified.txt");
        await Assert.That(held.Status).IsNull();
        await Assert.That(held.Tooltip!).Contains(ViewerSession.WroteItsFile);
        // Leading, so it is there in a column too narrow for the name
        await Assert.That(AsciiRenderer.Render(ScreenBuilder.Build(state))).Contains("| > ~ sample.verified");

        var locked = disk.Actions with
        {
            DeleteFile = static _ => throw new("The file is locked.")
        };
        state = ViewerSession.Apply(state, CommandKind.Accept, locked);

        var failed = QueueProjection.Rows(state).Single();
        await Assert.That(failed.Label).IsEqualTo("sample.verified.txt");
        await Assert.That(failed.Status).IsEqualTo("The file is locked.");
    }

    /// <summary>
    /// Accepted on its own it is carried out, held or not: that is the reviewer saying the file
    /// is redundant.
    /// </summary>
    [Test]
    public async Task A_held_delete_accepted_on_its_own_is_carried_out()
    {
        var disk = new Disk();
        var state = Queued(Fixtures.Move(), Delete());
        state = ViewerSession.Apply(state, CommandKind.AcceptAll, disk.Actions);

        state = ViewerSession.Apply(state, CommandKind.Accept, disk.Actions);

        await Assert.That(state.Queue).IsEmpty();
        await Assert.That(disk.Files).IsEmpty();
    }

    /// <summary>
    /// A delete raised again says nothing about when it was decided: a process of the same run
    /// that looked at the file before the move was accepted raises it after, as a later run
    /// would. Raising it again used to let go of the hold, and the next accept-all then deleted
    /// what had just been accepted. Both ways an arrival is taken, since the entry it is built
    /// from may or may not show what the queued one does. Nothing here is a file, so no stamp
    /// was read, and a stamp that is not known is not a file seen to differ.
    /// </summary>
    [Test]
    [Arguments(Fixtures.Expected)]
    [Arguments("something else")]
    public async Task A_delete_raised_again_is_still_held(string contentNow)
    {
        var disk = new Disk();
        var state = Queued(Fixtures.Move(), Delete());
        state = ViewerSession.Apply(state, CommandKind.AcceptAll, disk.Actions);

        state = ViewerSession.EnqueueTracked(state, Delete(contentNow));
        await Assert.That(state.Queue.Single().Status).IsEqualTo(ViewerSession.WroteItsFile);
        await Assert.That(ViewerSession.HeldReason(state.Queue, state.Queue.Single())).IsEqualTo(ViewerSession.WroteItsFile);

        state = ViewerSession.Apply(state, CommandKind.AcceptAll, disk.Actions);

        await Assert.That(state.Queue.Single().Kind).IsEqualTo(QueueEntryKind.Delete);
        await Assert.That(disk.Files[target]).IsEqualTo("received");
    }

    /// <summary>
    /// The same with files, where the stamps are: raised again over the file as the move left it
    /// the delete is still held, however many times, and raised over a file written since it is
    /// a delete like any other, since nothing of what the move put there is left to keep.
    /// </summary>
    [Test]
    public async Task A_delete_raised_again_is_let_go_only_over_a_file_written_since()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"MoveOntoDeleteTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var received = Path.Combine(directory, "sample.received.txt");
            var verified = Path.Combine(directory, "sample.verified.txt");
            await File.WriteAllTextAsync(received, "received");
            await File.WriteAllTextAsync(verified, "verified");
            var actions = Fixtures.Applied with
            {
                MoveFile = ViewerActions.Real.MoveFile,
                DeleteFile = ViewerActions.Real.DeleteFile
            };
            var state = Queued(TrackedEntry.ForMove(received, verified), TrackedEntry.ForDelete(verified));
            state = ViewerSession.Apply(state, CommandKind.AcceptAll, actions);
            await Assert.That(state.Queue.Single().WrittenAs).IsNotNull();

            // Twice: the first is built from what the file holds now, and the second finds an
            // entry already showing that
            for (var raise = 0; raise < 2; raise++)
            {
                state = ViewerSession.EnqueueTracked(state, TrackedEntry.DeleteAgain(state.Queue.Single(), verified));
                await Assert.That(state.Queue.Single().RightText).IsEqualTo("received");
                await Assert.That(state.Queue.Single().Status).IsEqualTo(ViewerSession.WroteItsFile);
                await Assert.That(ViewerSession.HeldReason(state.Queue, state.Queue.Single())).IsEqualTo(ViewerSession.WroteItsFile);
            }

            await File.WriteAllTextAsync(verified, "written by something else since");
            state = ViewerSession.EnqueueTracked(state, TrackedEntry.DeleteAgain(state.Queue.Single(), verified));
            await Assert.That(state.Queue.Single().Status).IsNull();
            await Assert.That(ViewerSession.HeldReason(state.Queue, state.Queue.Single())).IsNull();

            state = ViewerSession.Apply(state, CommandKind.AcceptAll, actions);

            await Assert.That(state.Queue).IsEmpty();
            await Assert.That(File.Exists(verified)).IsFalse();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// The entry an arrival is built from is read outside the lock, and may be from before the
    /// move was carried out. The hold is the queued entry's, whatever the arrival says of it.
    /// </summary>
    [Test]
    public async Task A_delete_raised_again_from_an_entry_read_before_the_move_is_still_held()
    {
        var disk = new Disk();
        var state = Queued(Fixtures.Move(), Delete());
        var readEarlier = state.Queue.Single(_ => _.Kind == QueueEntryKind.Delete);
        state = ViewerSession.Apply(state, CommandKind.AcceptAll, disk.Actions);

        state = ViewerSession.EnqueueTracked(state, readEarlier);

        await Assert.That(ViewerSession.HeldReason(state.Queue, state.Queue.Single())).IsEqualTo(ViewerSession.WroteItsFile);
        await Assert.That(state.Queue.Single().Status).IsEqualTo(ViewerSession.WroteItsFile);
    }

    /// <summary>
    /// A move that could not be carried out is still going to write the file, so its delete waits
    /// for it, and stops waiting if the move is discarded.
    /// </summary>
    [Test]
    public async Task A_delete_waits_for_a_move_that_is_still_pending()
    {
        var disk = new Disk();
        var locked = disk.Actions with
        {
            MoveFile = static (_, _) => throw new("The file is locked.")
        };
        var state = Queued(Fixtures.Move(), Delete());

        state = ViewerSession.Apply(state, CommandKind.AcceptAll, locked);

        await Assert.That(state.Queue.Select(_ => _.Kind)).IsEquivalentTo([QueueEntryKind.Move, QueueEntryKind.Delete]);
        var delete = state.Queue.Single(_ => _.Kind == QueueEntryKind.Delete);
        await Assert.That(delete.Status).IsEqualTo(ViewerSession.AwaitsItsFile);
        await Assert.That(disk.Files[target]).IsEqualTo("verified");

        state = ViewerSession.SelectKey(state, Fixtures.Move().Key);
        state = ViewerSession.Apply(state, CommandKind.Discard, disk.Actions);
        await Assert.That(ViewerSession.HeldReason(state.Queue, state.Queue.Single())).IsNull();

        state = ViewerSession.Apply(state, CommandKind.AcceptAll, disk.Actions);
        await Assert.That(state.Queue).IsEmpty();
        await Assert.That(disk.Files).IsEmpty();
    }

    /// <summary>
    /// "Accept all in" a header is the same batch, so it holds what an accept-all holds.
    /// </summary>
    [Test]
    public async Task A_group_accept_keeps_the_file_its_move_just_wrote()
    {
        var disk = new Disk();
        var other = Fixtures.Patch(Fixtures.SolutionFile("SolutionB", "Tests", "BTests.cs"), 10);
        var state = ViewerSession.EnqueueTracked(Fixtures.Inline(other), Fixtures.Move(solution: "SolutionA"));
        state = ViewerSession.EnqueueTracked(state, Delete(solution: "SolutionA"));
        var visible = QueueProjection.Visible(state, ScreenBuilder.BodyRows(state), out _).ToList();
        state = ViewerSession.OpenMenu(state, visible.FindIndex(_ => _.GroupName == "SolutionA"));

        var swept = ViewerSession.Apply(state, CommandKind.AcceptGroup, disk.Actions);

        await Assert.That(disk.Files[target]).IsEqualTo("received");
        await Assert.That(swept.Queue.Select(_ => _.Kind)).IsEquivalentTo([QueueEntryKind.Inline, QueueEntryKind.Delete]);
    }

    /// <summary>
    /// The watch over owned files reads a delete's file again once the move has written it, and
    /// the entry it builds from what is there now is still the delete that was held.
    /// </summary>
    [Test]
    public async Task A_held_delete_read_again_is_still_held()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"MoveOntoDeleteTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "sample.verified.txt");
            await File.WriteAllTextAsync(file, "verified");
            var queued = TrackedEntry.ForDelete(file) with
            {
                Written = true,
                Status = ViewerSession.WroteItsFile
            };

            await File.WriteAllTextAsync(file, "received");
            var again = TrackedEntry.DeleteAgain(queued, file);

            await Assert.That(again.RightText).IsEqualTo("received");
            await Assert.That(again.Written).IsTrue();
            await Assert.That(again.Status).IsEqualTo(ViewerSession.WroteItsFile);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    static SessionState Queued(params QueueEntry[] entries)
    {
        var state = SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows);
        foreach (var entry in entries)
        {
            state = ViewerSession.EnqueueTracked(state, entry);
        }

        return state;
    }

    /// <summary>
    /// A pending delete of the file <see cref="Fixtures.Move"/> is a move onto.
    /// </summary>
    static QueueEntry Delete(string content = Fixtures.Expected, string? solution = null) =>
        Fixtures.Delete("sample.verified.txt", solution, content);

    /// <summary>
    /// The files a test's actions act on, so what is asserted is what is left rather than which
    /// calls were made.
    /// </summary>
    sealed class Disk
    {
        public Dictionary<string, string> Files { get; } = new()
        {
            [temp] = "received",
            [target] = "verified"
        };

        public ViewerActions Actions =>
            Fixtures.Applied with
            {
                MoveFile = (from, to) =>
                {
                    Files[to] = Files[from];
                    Files.Remove(from);
                },
                DeleteFile = _ => Files.Remove(_)
            };
    }
}
