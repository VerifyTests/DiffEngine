/// <summary>
/// Several patches in one call. What that changes is how often a file is read and written, and
/// these pin the two things it must not change: what each patch is told, and what is in the file
/// afterwards.
/// </summary>
public class InlineApplierBatchTests
{
    /// <summary>
    /// The contract in one test: the same patches applied together and applied in turn leave the
    /// same bytes and report the same outcomes, in the same order. The batch includes the cases
    /// where a patch only reads as it does because of one before it - a call site the earlier
    /// literals have moved, the same patch twice, a second patch for a call site already taken.
    /// </summary>
    [Test]
    public async Task TogetherIsWhatInTurnLeaves()
    {
        using var together = new TempSource(Members(6));
        using var inTurn = new TempSource(Members(6));

        static InlinePatch[] Patches(string path) =>
        [
            Set(path, 0),
            Set(path, 3),
            // The same patch again, which finds its own literal already there
            Set(path, 3),
            // The call site the first one took, with different content: its anchor has gone
            Set(path, 0, content: "something else\nagain"),
            Set(path, 1),
            // Never was in the file
            Set(path, 2, anchor: "\"not in the source\""),
            Set(path, 5),
            Set(path, 4)
        ];

        var batch = InlineApplier.ApplyAll(Patches(together.FullName));
        // In turn, each asked about the line its call site is on by then: a batch brings a
        // patch's line along with the edits made above it, where every patch here was recorded
        // against the file as it started
        var single = new List<InlineApplyResult>();
        foreach (var patch in Patches(inTurn.FullName))
        {
            var line = single.Aggregate(patch.LineHint, (current, earlier) => earlier.Rebase(current));
            single.Add(InlineApplier.Apply(patch.At(line)));
        }

        await Assert.That(Statuses(batch)).IsEqualTo(Statuses(single));
        await Assert.That(Statuses(batch)).IsEqualTo("Applied, Applied, AlreadyApplied, NotFound, Applied, NotFound, Applied, Applied");
        await Assert.That(Messages(batch)).IsEqualTo(Messages(single));
        await Assert.That(together.Text).IsEqualTo(inTurn.Text);
        await Assert.That(together.Text).Contains("new 5");
    }

    /// <summary>
    /// The point of handing them over together: one file, one swap, however many of its snapshots
    /// are accepted.
    /// </summary>
    [Test]
    public async Task AFileIsSwappedOnceForAllItsPatches()
    {
        using var file = new TempSource(Members(4));
        var swaps = 0;

        var results = InlineApplier.ApplyAll(
            [Set(file.FullName, 0), Set(file.FullName, 1), Set(file.FullName, 2), Set(file.FullName, 3)],
            (temporary, destination) =>
            {
                swaps++;
                File.Replace(temporary, destination, null);
            });

        await Assert.That(Statuses(results)).IsEqualTo("Applied, Applied, Applied, Applied");
        await Assert.That(swaps).IsEqualTo(1);
        for (var member = 0; member < 4; member++)
        {
            await Assert.That(file.Text).Contains($"new {member}");
        }

        // And nothing of the temporary is left beside it
        await Assert.That(Directory.GetFileSystemEntries(file.Root)).IsEquivalentTo([file.FullName]);
    }

    /// <summary>
    /// Patches arrive in queue order, which is not file order. Each file is still read and written
    /// once, and each result is at the index its patch was given at.
    /// </summary>
    [Test]
    public async Task PatchesForSeveralFilesKeepTheirOrder()
    {
        using var first = new TempSource(Members(2));
        using var second = new TempSource(Members(2));
        var swapped = new List<string>();

        var results = InlineApplier.ApplyAll(
            [
                Set(first.FullName, 0),
                Set(second.FullName, 1, anchor: "\"not in the source\""),
                Set(first.FullName, 1),
                Set(second.FullName, 0)
            ],
            (temporary, destination) =>
            {
                swapped.Add(destination);
                File.Replace(temporary, destination, null);
            });

        await Assert.That(Statuses(results)).IsEqualTo("Applied, NotFound, Applied, Applied");
        await Assert.That(string.Join(", ", swapped)).IsEqualTo($"{first.FullName}, {second.FullName}");
        await Assert.That(first.Text).Contains("new 0");
        await Assert.That(first.Text).Contains("new 1");
        await Assert.That(second.Text).Contains("new 0");
        await Assert.That(second.Text).Contains("\"old 1\"");
    }

    /// <summary>
    /// One write carries every edit, so when it fails none of them happened and each patch has to
    /// say so: an entry reported as applied is dropped from its queue, and these are not in the
    /// source. A patch judged before the first edit keeps its answer, which was about the file as
    /// it is. One judged after does not, since what it was judged against was never written - the
    /// duplicate here was only already applied because of the patch before it.
    /// </summary>
    [Test]
    public async Task AWriteThatFailsFailsEveryPatchItCarried()
    {
        using var file = new TempSource(Members(3));
        var before = file.Text;

        var results = InlineApplier.ApplyAll(
            [
                Set(file.FullName, 2, anchor: "\"not in the source\""),
                Set(file.FullName, 0),
                Set(file.FullName, 0),
                Set(file.FullName, 1)
            ],
            (_, _) => throw new IOException("The process cannot access the file."));

        await Assert.That(Statuses(results)).IsEqualTo("NotFound, Failed, Failed, Failed");
        await Assert.That(results[1].Message!).Contains("Failed to write");
        await Assert.That(results[1].Exception).IsTypeOf<IOException>();
        await Assert.That(file.Text).IsEqualTo(before);
        await Assert.That(Directory.GetFileSystemEntries(file.Root)).IsEquivalentTo([file.FullName]);
    }

    /// <summary>
    /// A patch after the first edit that made no edit of its own is told what is true of the file
    /// as it still is, and not that the write failed: its snapshot was in the source all along,
    /// or its call site was never there. They used to report the write with the rest, so the
    /// first stayed queued as a failure and the second was not told to re-run. The duplicate
    /// still reports the write, since the literal it found was one the write was carrying.
    /// </summary>
    [Test]
    public async Task AWriteThatFailsLeavesAPatchThatDidNotNeedItItsOwnAnswer()
    {
        using var file = new TempSource(Members(4));
        var before = file.Text;

        var results = InlineApplier.ApplyAll(
            [
                Set(file.FullName, 0),
                Set(file.FullName, 2, anchor: "\"not in the source\""),
                Set(file.FullName, 1, content: "old 1"),
                Set(file.FullName, 0),
                Set(file.FullName, 3)
            ],
            (_, _) => throw new IOException("The process cannot access the file."));

        await Assert.That(Statuses(results)).IsEqualTo("Failed, NotFound, AlreadyApplied, Failed, Failed");
        await Assert.That(results[1].Message!).Contains("Re-run the test");
        await Assert.That(results[3].Message!).Contains("Failed to write");
        await Assert.That(file.Text).IsEqualTo(before);
    }

    /// <summary>
    /// A batch in which nothing applies writes nothing, as a single patch that is already applied
    /// writes nothing: the file an editor has open is not touched for no reason.
    /// </summary>
    [Test]
    public async Task NothingToApplyIsNothingWritten()
    {
        using var file = new TempSource(Members(2));
        var swaps = 0;

        var results = InlineApplier.ApplyAll(
            [
                Set(file.FullName, 0, anchor: "\"not in the source\""),
                Set(file.FullName, 1, content: "old 1")
            ],
            (_, _) => swaps++);

        await Assert.That(Statuses(results)).IsEqualTo("NotFound, AlreadyApplied");
        await Assert.That(swaps).IsEqualTo(0);
    }

    /// <summary>
    /// A patch that names no file, or a file that is not there, is that patch's failure. The ones
    /// around it are for other files and are applied as if it had not been in the batch.
    /// </summary>
    [Test]
    public async Task APatchWithNoFileToPatchDoesNotStopTheRest()
    {
        using var file = new TempSource(Members(2));
        var missing = Path.Combine(file.Root, "NotThere.cs");

        var results = InlineApplier.ApplyAll(
        [
            Set(file.FullName, 0),
            Set("", 0),
            Set(missing, 0),
            // No line to look near, which is a patch nothing can be done with
            new(file.FullName, 0, "\"old 1\"", "new 1")
            {
                TestName = null
            },
            Set(file.FullName, 1)
        ]);

        await Assert.That(Statuses(results)).IsEqualTo("Applied, Failed, Failed, Failed, Applied");
        await Assert.That(results[1].Message!).Contains("SourceFile is empty");
        await Assert.That(results[2].Message!).Contains("does not exist");
        await Assert.That(results[3].Message!).Contains("LineHint");
        await Assert.That(file.Text).Contains("new 0");
        await Assert.That(file.Text).Contains("new 1");
    }

    /// <summary>
    /// The file is decoded once and encoded once for the whole batch, and comes back as it was
    /// read: the same encoding, the same byte order mark, the same line endings.
    /// </summary>
    [Test]
    public async Task EncodingAndLineEndingsSurviveABatch()
    {
        var encoding = new UnicodeEncoding(false, true);
        using var file = new TempSource(Members(3, "\r\n"), encoding);

        var results = InlineApplier.ApplyAll([Set(file.FullName, 0), Set(file.FullName, 1), Set(file.FullName, 2)]);

        await Assert.That(Statuses(results)).IsEqualTo("Applied, Applied, Applied");
        var bytes = await File.ReadAllBytesAsync(file.FullName);
        await Assert.That(bytes[0]).IsEqualTo((byte) 0xFF);
        await Assert.That(bytes[1]).IsEqualTo((byte) 0xFE);
        var text = encoding.GetString(bytes, 2, bytes.Length - 2);
        await Assert.That(text).Contains("new 2");
        await Assert.That(text.Replace("\r\n", "")).DoesNotContain("\n");
    }

    /// <summary>
    /// An applied patch says which lines it moved, so whoever holds other patches for the file
    /// can bring their lines along: the lines under the call site, by what the literal grew, and
    /// none at or above it.
    /// </summary>
    [Test]
    public async Task AnAppliedPatchSaysWhichLinesItMoved()
    {
        using var file = new TempSource(Members(4));

        var result = InlineApplier.Apply(Set(file.FullName, 1));

        await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
        await Assert.That(result.MovedBy).IsGreaterThan(0);
        var lines = file.Text.Split('\n');
        // Each member was on line member + 3
        await Assert.That(lines[result.Rebase(3) - 1]).Contains("void M0()");
        await Assert.That(lines[result.Rebase(4) - 1]).Contains("void M1()");
        await Assert.That(lines[result.Rebase(5) - 1]).Contains("void M2()");
        await Assert.That(lines[result.Rebase(6) - 1]).Contains("void M3()");
        await Assert.That(result.Rebase(4)).IsEqualTo(4);
        await Assert.That(result.Rebase(5)).IsEqualTo(5 + result.MovedBy);
    }

    /// <summary>
    /// A literal that gets shorter moves the lines under it up, and one that stays as long moves
    /// none.
    /// </summary>
    [Test]
    public async Task ALiteralThatShrinksMovesTheLinesUnderItUp()
    {
        using var file = new TempSource(Members(3));
        var grown = InlineApplier.Apply(Set(file.FullName, 0));

        var shrunk = InlineApplier.Apply(
            new(file.FullName, 3, null, "one line")
            {
                TestName = null,
                MemberName = "M0",
                OriginalValue = "new 0\nsecond line"
            });
        var same = InlineApplier.Apply(
            new(file.FullName, 3, null, "another")
            {
                TestName = null,
                MemberName = "M0",
                OriginalValue = "one line"
            });

        await Assert.That(shrunk.Status).IsEqualTo(InlineApplyStatus.Applied);
        await Assert.That(shrunk.MovedBy).IsLessThan(0);
        await Assert.That(shrunk.MovedBy).IsGreaterThan(-grown.MovedBy - 1);
        var lines = file.Text.Split('\n');
        await Assert.That(lines[shrunk.Rebase(grown.Rebase(5)) - 1]).Contains("void M2()");
        await Assert.That(same.Status).IsEqualTo(InlineApplyStatus.Applied);
        await Assert.That(same.MovedBy).IsEqualTo(0);
        await Assert.That(same).IsSameReferenceAs(InlineApplyResult.Applied);
    }

    /// <summary>
    /// Each result of a batch counts lines as the patches before it left them, so rebasing a
    /// line through the results in order gives where it is in the file that was written.
    /// </summary>
    [Test]
    public async Task ABatchsResultsRebaseALineInOrder()
    {
        using var file = new TempSource(Members(5));

        var results = InlineApplier.ApplyAll([Set(file.FullName, 2), Set(file.FullName, 0), Set(file.FullName, 3)]);

        await Assert.That(Statuses(results)).IsEqualTo("Applied, Applied, Applied");
        var lines = file.Text.Split('\n');
        for (var member = 0; member < 5; member++)
        {
            var line = results.Aggregate(member + 3, (current, result) => result.Rebase(current));
            await Assert.That(lines[line - 1]).Contains($"void M{member}()");
        }
    }

    /// <summary>
    /// An Append is the patch that cannot do without its line: with no anchor, a line that names
    /// no call leaves it to choose among the member's calls, and it is refused where there are
    /// two. In a batch the snapshot accepted above it had moved that line, so the second
    /// snapshot of a file was refused for the accept of the first.
    /// </summary>
    [Test]
    public async Task AnAppendUnderAnEarlierEditIsAskedAboutTheLineItsCallIsOnNow()
    {
        using var file = new TempSource(
            """
            class C
            {
                void M0() => Verify(value0).Snapshot("old 0");
                Task M1()
                {
                    Verify(first);
                    return Verify(second);
                }
            }
            """);
        InlinePatch[] patches =
        [
            Set(file.FullName, 0),
            new(file.FullName, 7, null, "appended", InlinePatchMode.Append)
            {
                TestName = null,
                MemberName = "M1"
            }
        ];

        var results = InlineApplier.ApplyAll(patches);

        await Assert.That(Statuses(results)).IsEqualTo("Applied, Applied");
        await Assert.That(file.Text).Contains("Verify(second)");
        await Assert.That(file.Text).Contains("Verify(first);");
        await Assert.That(file.Text).DoesNotContain("Verify(second);");
    }

    [Test]
    public async Task AnEmptyBatchIsNoResults() =>
        await Assert.That(InlineApplier.ApplyAll([])).IsEmpty();

    /// <summary>
    /// A patch that stops being wanted while its file is waited for is not in what is written.
    /// A queue owner hands a file's snapshots over together, and one discarded, or settled by a
    /// test that started passing, before the write went into the source with the rest. The file
    /// is what it would be had the patch never been handed over, and so is what every other
    /// patch is told, the lines each moved included: whoever holds what is left of the file
    /// brings it along by those.
    /// </summary>
    [Test]
    public async Task AnUnwantedPatchIsNotInWhatIsWritten()
    {
        using var asked = new TempSource(Members(4));
        using var without = new TempSource(Members(4));
        var swaps = 0;

        var results = InlineApplier.ApplyAll(
            [Set(asked.FullName, 0), Set(asked.FullName, 1), Set(asked.FullName, 2), Set(asked.FullName, 3)],
            (temporary, destination) =>
            {
                swaps++;
                File.Replace(temporary, destination, null);
            },
            _ => _ != 1);
        var control = InlineApplier.ApplyAll([Set(without.FullName, 0), Set(without.FullName, 2), Set(without.FullName, 3)]);

        await Assert.That(Statuses(results)).IsEqualTo("Applied, Withdrawn, Applied, Applied");
        await Assert.That(asked.Text).IsEqualTo(without.Text);
        await Assert.That(asked.Text).Contains("\"old 1\"");
        await Assert.That(asked.Text).DoesNotContain("new 1");
        await Assert.That(Outcomes([results[0], results[2], results[3]])).IsEqualTo(Outcomes(control));
        await Assert.That(results[1].MovedBy).IsEqualTo(0);
        await Assert.That(swaps).IsEqualTo(1);
        // Every member is where the results taken in order say it is, the one left alone too
        var lines = asked.Text.Split('\n');
        for (var member = 0; member < 4; member++)
        {
            var line = results.Aggregate(member + 3, (current, result) => result.Rebase(current));
            await Assert.That(lines[line - 1]).Contains($"void M{member}()");
        }
    }

    /// <summary>
    /// The same over a batch where a patch reads as it does because of one before it: the same
    /// patch twice, a second patch for a call site already taken, a call site that was never
    /// there. Whichever are taken back, the file and every outcome left are those of the batch
    /// without them. A patch that made no edit is not asked about, since there is nothing of it
    /// to leave out, and keeps the answer it had.
    /// </summary>
    [Test]
    [Arguments("0")]
    [Arguments("1")]
    [Arguments("2")]
    [Arguments("3")]
    [Arguments("4")]
    [Arguments("5")]
    [Arguments("6")]
    [Arguments("7")]
    [Arguments("0 1")]
    [Arguments("1 2")]
    [Arguments("0 3")]
    [Arguments("4 6 7")]
    [Arguments("0 1 2 3 4 5 6 7")]
    public async Task WithoutTheUnwantedIsWhatNeverHandingThemOverLeaves(string takenBack)
    {
        var unwanted = takenBack.Split(' ').Select(int.Parse).ToList();
        using var asked = new TempSource(Members(6));
        using var without = new TempSource(Members(6));

        static InlinePatch[] Patches(string path) =>
        [
            Set(path, 0),
            Set(path, 3),
            // The same patch again, which finds its own literal already there
            Set(path, 3),
            // The call site the first one took, with different content: its anchor has gone
            Set(path, 0, content: "something else\nagain"),
            Set(path, 1),
            // Never was in the file
            Set(path, 2, anchor: "\"not in the source\""),
            Set(path, 5),
            Set(path, 4)
        ];

        var questions = new List<int>();
        var results = InlineApplier.ApplyAll(
            Patches(asked.FullName),
            _ =>
            {
                questions.Add(_);
                return !unwanted.Contains(_);
            });
        var kept = Enumerable.Range(0, 8).Where(_ => !unwanted.Contains(_)).ToList();
        var all = Patches(without.FullName);
        var control = InlineApplier.ApplyAll(kept.Select(_ => all[_]).ToList());

        await Assert.That(asked.Text).IsEqualTo(without.Text);
        await Assert.That(Outcomes(kept.Select(_ => results[_]))).IsEqualTo(Outcomes(control));
        // Once each at most, and only of a patch with an edit to leave out
        await Assert.That(questions.Distinct().Count()).IsEqualTo(questions.Count);
        foreach (var index in unwanted)
        {
            if (questions.Contains(index))
            {
                await Assert.That(results[index].Status).IsEqualTo(InlineApplyStatus.Withdrawn);
            }
            else
            {
                await Assert.That(results[index].Status).IsNotEqualTo(InlineApplyStatus.Applied);
                await Assert.That(results[index].Status).IsNotEqualTo(InlineApplyStatus.Withdrawn);
            }
        }
    }

    /// <summary>
    /// The second of two patches for one call site makes no edit while the first is there, and
    /// is the one that edits once the first is taken back. It had not been asked about, so it
    /// is asked then, and left out as well when it is not wanted either.
    /// </summary>
    [Test]
    public async Task APatchThatOnlyEditsOnceAnotherIsTakenBackIsAskedAboutToo()
    {
        using var file = new TempSource(Members(3));
        using var without = new TempSource(Members(3));
        var questions = new List<int>();

        var results = InlineApplier.ApplyAll(
            [Set(file.FullName, 1), Set(file.FullName, 1), Set(file.FullName, 2)],
            _ =>
            {
                questions.Add(_);
                return _ == 2;
            });
        var control = InlineApplier.ApplyAll([Set(without.FullName, 2)]);

        await Assert.That(string.Join(", ", questions)).IsEqualTo("0, 2, 1");
        await Assert.That(Statuses(results)).IsEqualTo("Withdrawn, Withdrawn, Applied");
        await Assert.That(file.Text).IsEqualTo(without.Text);
        await Assert.That(file.Text).Contains("\"old 1\"");
        await Assert.That(Outcomes([results[2]])).IsEqualTo(Outcomes(control));
    }

    /// <summary>
    /// The question is asked once the file is patched in memory and before it is written, which
    /// is as late as it can be asked: the file on disk is still as it was read.
    /// </summary>
    [Test]
    public async Task TheQuestionIsAskedBeforeAnythingIsWritten()
    {
        using var file = new TempSource(Members(3));
        var before = file.Text;
        var onDisk = new List<string>();
        var swaps = 0;

        InlineApplier.ApplyAll(
            [Set(file.FullName, 0), Set(file.FullName, 1), Set(file.FullName, 2)],
            (temporary, destination) =>
            {
                swaps++;
                File.Replace(temporary, destination, null);
            },
            _ =>
            {
                onDisk.Add($"{swaps} {file.Text == before}");
                return true;
            });

        await Assert.That(string.Join(", ", onDisk)).IsEqualTo("0 True, 0 True, 0 True");
        await Assert.That(swaps).IsEqualTo(1);
    }

    /// <summary>
    /// With every edit taken back there is nothing to write, and the file an editor has open is
    /// not touched.
    /// </summary>
    [Test]
    public async Task NothingWantedIsNothingWritten()
    {
        using var file = new TempSource(Members(2));
        var before = file.Text;
        var swaps = 0;

        var results = InlineApplier.ApplyAll(
            [Set(file.FullName, 0), Set(file.FullName, 1, content: "old 1"), Set(file.FullName, 1)],
            (_, _) => swaps++,
            _ => false);

        await Assert.That(Statuses(results)).IsEqualTo("Withdrawn, AlreadyApplied, Withdrawn");
        await Assert.That(swaps).IsEqualTo(0);
        await Assert.That(file.Text).IsEqualTo(before);
    }

    /// <summary>
    /// A patch taken back is asked about where it was in what the caller handed over, whichever
    /// file it is for, and the other files are written as they were going to be.
    /// </summary>
    [Test]
    public async Task APatchIsAskedAboutByWhereTheCallerPutIt()
    {
        using var first = new TempSource(Members(2));
        using var second = new TempSource(Members(2));

        var results = InlineApplier.ApplyAll(
            [
                Set(first.FullName, 0),
                Set(second.FullName, 1),
                Set(first.FullName, 1),
                Set(second.FullName, 0)
            ],
            _ => _ != 2);

        await Assert.That(Statuses(results)).IsEqualTo("Applied, Applied, Withdrawn, Applied");
        await Assert.That(first.Text).Contains("new 0");
        await Assert.That(first.Text).Contains("\"old 1\"");
        await Assert.That(second.Text).Contains("new 0");
        await Assert.That(second.Text).Contains("new 1");
    }

    /// <summary>
    /// A write that fails fails what it was carrying, and a patch taken back before it was not
    /// among that: nothing of it was going to be written either way.
    /// </summary>
    [Test]
    public async Task AWriteThatFailsLeavesAPatchTakenBackAsItWas()
    {
        using var file = new TempSource(Members(3));
        var before = file.Text;

        var results = InlineApplier.ApplyAll(
            [Set(file.FullName, 0), Set(file.FullName, 1), Set(file.FullName, 2)],
            (_, _) => throw new IOException("The process cannot access the file."),
            _ => _ != 1);

        await Assert.That(Statuses(results)).IsEqualTo("Failed, Withdrawn, Failed");
        await Assert.That(file.Text).IsEqualTo(before);
    }

    /// <summary>
    /// A question that throws is taken as yes: the patch was handed over to be written, and the
    /// outcomes of the patches beside it are not lost to it.
    /// </summary>
    [Test]
    public async Task AQuestionThatThrowsIsTakenAsWanted()
    {
        using var file = new TempSource(Members(2));

        var results = InlineApplier.ApplyAll(
            [Set(file.FullName, 0), Set(file.FullName, 1)],
            _ => throw new InvalidOperationException("the queue went away"));

        await Assert.That(Statuses(results)).IsEqualTo("Applied, Applied");
        await Assert.That(file.Text).Contains("new 1");
    }

    // Everything a host reads off a result, the lines it moved included
    static string Outcomes(IEnumerable<InlineApplyResult> results) =>
        string.Join("\n", results.Select(_ => $"{_.Status} {_.MovedFrom} {_.MovedBy} {_.Message ?? "-"}"));

    /// <summary>
    /// A batch lexes its file once and carries the scan from each patch to the next. What each
    /// patch is told, and what the source comes to, has to be what lexing the whole text again
    /// for every patch gives. Patches of every mode, in an order made at random: literals that
    /// grow and move every call site under them, calls appended and removed, the same patch
    /// twice, an anchor that is in no call, and hints that are stale from the first edit on.
    /// </summary>
    [Test]
    [Arguments("cs")]
    [Arguments("fs")]
    public async Task ACarriedScanTellsEachPatchWhatLexingAgainWould(string extension)
    {
        var language = SourceLanguage.ForFile($"Sample.{extension}");
        var path = Path.Combine(Path.GetTempPath(), $"Sample.{extension}");
        var problems = new List<string>();
        var seen = new HashSet<InlineApplyStatus>();
        for (var seed = 0; seed < 60 && problems.Count < 3; seed++)
        {
            var random = new Random(seed);
            var (source, patches) = MixedBatch(random, extension, path, seed % 2 == 0 ? "\n" : "\r\n");

            var carried = source;
            var results = new InlineApplyResult[patches.Count];
            InlineApplier.PatchInTurn(language, ref carried, patches, path, results);

            var lexedAgain = source;
            // Where each edit so far moved the lines, worked out here from the two texts, since
            // a patch is asked about the line its call site has been moved to
            var moves = new List<(int From, int By)>();
            for (var index = 0; index < patches.Count; index++)
            {
                var patch = patches[index];
                var line = patch.LineHint;
                foreach (var (from, by) in moves)
                {
                    if (line >= from)
                    {
                        line += by;
                    }
                }

                var status = InlinePatcher.TryApply(
                    language,
                    lexedAgain,
                    line,
                    patch.Mode,
                    patch.OriginalExpression,
                    patch.OriginalValue,
                    patch.MemberName,
                    patch.EntryPoints,
                    false,
                    patch.NewContent,
                    out var patched,
                    out var reason);
                if (status == PatchStatus.Applied)
                {
                    moves.Add(Moved(lexedAgain, patched));
                    lexedAgain = patched;
                }

                var expected = status switch
                {
                    PatchStatus.Applied => InlineApplyResult.Applied,
                    PatchStatus.AlreadyApplied => InlineApplyResult.AlreadyApplied,
                    _ => InlineApplyResult.NotFound(reason)
                };
                if (results[index].Status != expected.Status ||
                    results[index].Message != expected.Message)
                {
                    problems.Add($"seed {seed}, patch {index} ({patch.Mode} at {patch.LineHint}): {results[index].Status} {results[index].Message}, and lexed again {expected.Status} {expected.Message}");
                    break;
                }
            }

            if (carried != lexedAgain)
            {
                problems.Add($"seed {seed}: the source differs");
            }

            seen.UnionWith(results.Select(_ => _.Status));
        }

        await Assert.That(problems).IsEmpty();
        // Or the batches were all refusals, and agreed about nothing much
        await Assert.That(seen).IsEquivalentTo([InlineApplyStatus.Applied, InlineApplyStatus.AlreadyApplied, InlineApplyStatus.NotFound]);
    }

    /// <summary>
    /// Which lines an edit moved, the slow way: the first line that is whole in what the two
    /// texts end with alike, and how many more lines there are.
    /// </summary>
    static (int From, int By) Moved(string before, string after)
    {
        var beforeLines = before.Split('\n');
        var afterLines = after.Split('\n');
        var limit = Math.Min(beforeLines.Length, afterLines.Length);
        var same = 0;
        while (same < limit &&
               beforeLines[beforeLines.Length - 1 - same] == afterLines[afterLines.Length - 1 - same])
        {
            same++;
        }

        // Counted from the end, so the lines of a literal that were in both texts are not the
        // edit's. The first of them is 1 based
        return (beforeLines.Length - same + 1, afterLines.Length - beforeLines.Length);
    }

    /// <summary>
    /// A file of members, each with a call that has a snapshot and one that has none, and a
    /// shuffled batch of patches for them.
    /// </summary>
    static (string source, List<InlinePatch> patches) MixedBatch(Random random, string extension, string path, string eol)
    {
        var fsharp = extension == "fs";
        var builder = new StringBuilder();
        var patches = new List<InlinePatch>();
        var line = 0;

        void Add(string text)
        {
            builder.Append(text);
            builder.Append(eol);
            line++;
        }

        Add(fsharp ? "module Tests" : "class Tests");
        Add(fsharp ? "" : "{");
        var members = 6 + random.Next(6);
        for (var member = 0; member < members; member++)
        {
            Add(fsharp ? $"let member{member} () =" : $"    async Task Member{member}()");
            Add(fsharp ? "    // a comment with a \" in it" : "    {");
            Add($"        {(fsharp ? "let" : "var")} text{member} = \"a string // with (* things */ in it\"{(fsharp ? "" : ";")}");
            Add($"        Verify(first{member})");
            Add($"            .Snapshot(\"old {member}\"){(fsharp ? ".ToTask() |> ignore" : ";")}");
            var snapshotLine = line;
            Add($"        Verify(second{member}){(fsharp ? ".ToTask() |> ignore" : ";")}");
            var verifyLine = line;
            Add(fsharp ? "" : "    }");

            var name = fsharp ? $"member{member}" : $"Member{member}";
            InlinePatch Patch(int hint, string? expression, string? value, string content, InlinePatchMode mode) =>
                new(path, hint, fsharp ? null : expression, content, mode)
                {
                    TestName = null,
                    MemberName = name,
                    OriginalValue = fsharp ? value : null
                };

            var content = random.Next(3) switch
            {
                0 => $"new {member}",
                1 => $"new {member}\nsecond \"line\"\n    third",
                _ => $"// {member}\n(* \"\"\" *)\n"
            };
            switch (random.Next(5))
            {
                case 0:
                    patches.Add(Patch(snapshotLine, $"\"old {member}\"", $"old {member}", content, InlinePatchMode.Set));
                    break;
                case 1:
                    patches.Add(Patch(snapshotLine, $"\"old {member}\"", $"old {member}", "", InlinePatchMode.Remove));
                    break;
                case 2:
                    patches.Add(Patch(verifyLine, null, null, content, InlinePatchMode.Append));
                    break;
                case 3:
                    // Both of a member's calls, and the first of them twice
                    patches.Add(Patch(snapshotLine, $"\"old {member}\"", $"old {member}", content, InlinePatchMode.Set));
                    patches.Add(Patch(verifyLine, null, null, content, InlinePatchMode.Append));
                    patches.Add(Patch(snapshotLine, $"\"old {member}\"", $"old {member}", content, InlinePatchMode.Set));
                    break;
                default:
                    patches.Add(Patch(snapshotLine, "\"in no call\"", "in no call", content, InlinePatchMode.Set));
                    break;
            }
        }

        Add(fsharp ? "" : "}");

        // Queue order is not file order
        for (var index = patches.Count - 1; index > 0; index--)
        {
            var other = random.Next(index + 1);
            (patches[index], patches[other]) = (patches[other], patches[index]);
        }

        return (builder.ToString(), patches);
    }

    // Joined, because the order is the point and the collection assertions do not check it
    static string Statuses(IEnumerable<InlineApplyResult> results) =>
        string.Join(", ", results.Select(_ => _.Status));

    static string Messages(IEnumerable<InlineApplyResult> results) =>
        string.Join("\n", results.Select(_ => _.Message ?? "-"));

    /// <summary>
    /// A member a line, each with a snapshot of its own. The content a patch writes is two lines,
    /// so it is rendered as a literal on lines of its own and every member below it moves.
    /// </summary>
    static string Members(int count, string eol = "\n")
    {
        var builder = new StringBuilder();
        builder.Append($"class C{eol}{{{eol}");
        for (var member = 0; member < count; member++)
        {
            builder.Append($"    void M{member}() => Verify(value{member}).Snapshot(\"old {member}\");{eol}");
        }

        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>
    /// The patch a failing run would have produced for a member of <see cref="Members"/>: its line
    /// as it was when the file was written, the literal it held, and the member it is in.
    /// </summary>
    static InlinePatch Set(string path, int member, string? anchor = null, string? content = null) =>
        new(path, member + 3, anchor ?? $"\"old {member}\"", content ?? $"new {member}\nsecond line")
        {
            TestName = null,
            MemberName = $"M{member}"
        };

    /// <summary>
    /// A source file in a directory of its own, so what is left beside it can be asserted. Root
    /// and FullName rather than Directory and Path, both of which are types the tests use.
    /// </summary>
    sealed class TempSource : IDisposable
    {
        readonly Encoding encoding;

        public TempSource(string text, Encoding? encoding = null)
        {
            this.encoding = encoding ?? new UTF8Encoding(false);
            Root = Path.Combine(Path.GetTempPath(), $"InlineApplierBatchTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            FullName = Path.Combine(Root, "Sample.cs");
            File.WriteAllBytes(FullName, [.. this.encoding.GetPreamble(), .. this.encoding.GetBytes(text)]);
        }

        public string Root { get; }

        public string FullName { get; }

        public string Text
        {
            get
            {
                var bytes = File.ReadAllBytes(FullName);
                var preamble = encoding.GetPreamble().Length;
                return encoding.GetString(bytes, preamble, bytes.Length - preamble);
            }
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, true);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                // Best effort cleanup of the temp directory
            }
        }
    }
}
