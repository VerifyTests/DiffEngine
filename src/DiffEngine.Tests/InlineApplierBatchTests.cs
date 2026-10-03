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

        InlinePatch[] Patches(string path) =>
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
        var single = Patches(inTurn.FullName)
            .Select(InlineApplier.Apply)
            .ToList();

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

    [Test]
    public async Task AnEmptyBatchIsNoResults() =>
        await Assert.That(InlineApplier.ApplyAll([])).IsEmpty();

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
            for (var index = 0; index < patches.Count; index++)
            {
                var patch = patches[index];
                var status = InlinePatcher.TryApply(
                    language,
                    lexedAgain,
                    patch.LineHint,
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
