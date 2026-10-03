/// <summary>
/// Persisting a queue back to disk is what stands between "the owner exited" and "every pending
/// snapshot silently gone", so these pin the layout accept tooling reads: the file trio, where it
/// lands relative to the source's project, and the naming that carries the framework label.
/// </summary>
public class InlineStagingTests
{
    [Test]
    public async Task WritesTheTrioUnderTheProjectsObj()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");

        var patch = Patch(source, "line one\nline two", framework: "net10.0");
        var written = InlineStaging.Persist([new(patch)]);

        await Assert.That(written).IsEqualTo(1);

        var files = project.StagedFiles();
        await Assert.That(files.Count).IsEqualTo(3);

        var patchFile = files.Single(_ => _.EndsWith(".inlinepatch"));
        // The framework rides the name's last dot segment, dots folded to underscores so the
        // label survives being read back off the file name.
        await Assert.That(Path.GetFileName(patchFile))
            .IsEqualTo($"SampleTests.Sample.{Hash(source)}.net10_0.inlinepatch");

        await Assert.That(InlinePatchFile.TryRead(patchFile, out var read)).IsTrue();
        await Assert.That(read!.SourceFile).IsEqualTo(patch.SourceFile);
        await Assert.That(read.LineHint).IsEqualTo(patch.LineHint);
        await Assert.That(read.NewContent).IsEqualTo(patch.NewContent);
        await Assert.That(read.OriginalValue).IsEqualTo(patch.OriginalValue);
        await Assert.That(read.Framework).IsEqualTo("net10.0");

        await Assert.That(await File.ReadAllTextAsync(files.Single(_ => _.EndsWith(".received.txt"))))
            .IsEqualTo("line one\nline two");
        await Assert.That(await File.ReadAllTextAsync(files.Single(_ => _.EndsWith(".expected.txt"))))
            .IsEqualTo("old");
    }

    [Test]
    public async Task PersistingAgainOverwritesRatherThanAccumulates()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");

        InlineStaging.Persist([new(Patch(source, "first", framework: "net10.0"))]);
        InlineStaging.Persist([new(Patch(source, "second", framework: "net10.0"))]);

        var files = project.StagedFiles();
        await Assert.That(files.Count).IsEqualTo(3);
        await Assert.That(await File.ReadAllTextAsync(files.Single(_ => _.EndsWith(".received.txt"))))
            .IsEqualTo("second");
    }

    [Test]
    public async Task ConflictedEntryKeepsEachFrameworksContent()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");

        var entry = new PendingInline(
        [
            new(Patch(source, "from net8", framework: "net8.0"), ["net8.0"]),
            new(Patch(source, "from net10", framework: "net10.0"), ["net10.0"]),
        ]);

        var written = InlineStaging.Persist([entry]);

        // One trio per variant, distinct by the framework segment, so a reader regrouping by call
        // site sees the disagreement instead of one framework's content standing for both.
        await Assert.That(written).IsEqualTo(2);
        var names = project.StagedFiles().Select(Path.GetFileName).ToList();
        await Assert.That(names.Count(_ => _!.Contains(".net8_0."))).IsEqualTo(3);
        await Assert.That(names.Count(_ => _!.Contains(".net10_0."))).IsEqualTo(3);
    }

    [Test]
    public async Task SourceWithNoProjectAboveItIsSkipped()
    {
        // A path from another machine, or a project deleted since the run: nowhere honest to
        // stage, and skipped is better than a guess.
        var source = Path.Combine(Path.GetTempPath(), $"inline-staging-none-{Guid.NewGuid():N}", "SampleTests.cs");

        var written = InlineStaging.Persist([new(Patch(source, "content"))]);

        await Assert.That(written).IsEqualTo(0);
    }

    [Test]
    public async Task RemoveIsNeverPersisted()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");

        var remove = new InlinePatch(source, 42, "\"old\"", "", InlinePatchMode.Remove)
        {
            TestName = null,
            OriginalValue = "old"
        };

        var written = InlineStaging.Persist([new(remove)]);

        await Assert.That(written).IsEqualTo(0);
        await Assert.That(project.StagedFiles()).IsEmpty();
    }

    [Test]
    public async Task UnlabeledPatchStillPersists()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");

        var written = InlineStaging.Persist([new(Patch(source, "content"))]);

        await Assert.That(written).IsEqualTo(1);
        var patchFile = project.StagedFiles().Single(_ => _.EndsWith(".inlinepatch"));
        await Assert.That(Path.GetFileName(patchFile)).EndsWith(".unknown.inlinepatch");
    }

    /// <summary>
    /// A settle reaches the queue owner, and says nothing to a snapshot sitting on disk. Clearing
    /// is what stops a staged snapshot outliving the run that made it stale.
    /// </summary>
    [Test]
    public async Task ClearRemovesTheTrioForTheCallSite()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        InlineStaging.Persist([new(Patch(source, "content", framework: "net10.0"))]);

        var cleared = InlineStaging.Clear(source, 42, null);

        await Assert.That(cleared).IsEqualTo(1);
        await Assert.That(project.StagedFiles()).IsEmpty();
    }

    /// <summary>
    /// Every framework's trio goes, because the call site is what settled, not one framework's
    /// reading of it.
    /// </summary>
    [Test]
    public async Task ClearRemovesEveryFrameworksTrio()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        InlineStaging.Persist(
        [
            new(
            [
                new(Patch(source, "from net8", framework: "net8.0"), ["net8.0"]),
                new(Patch(source, "from net10", framework: "net10.0"), ["net10.0"]),
            ])
        ]);

        var cleared = InlineStaging.Clear(source, 42, null);

        await Assert.That(cleared).IsEqualTo(2);
        await Assert.That(project.StagedFiles()).IsEmpty();
    }

    /// <summary>
    /// A settle that names the framework it came from takes only that framework's trio. The queue
    /// has always scoped a settle this way; staging is the same situation and had no way to say
    /// it, so in a net8;net10 run where net10 started passing and net8 did not, net10's settle
    /// deleted net8's still-failing snapshot and it was then pending nowhere.
    /// </summary>
    [Test]
    public async Task ClearScopedToAnOriginLeavesTheOtherFrameworkStaged()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        InlineStaging.Persist(
        [
            new(
            [
                new(Patch(source, "from net8", framework: "net8.0"), ["net8.0"]),
                new(Patch(source, "from net10", framework: "net10.0"), ["net10.0"]),
            ])
        ]);

        var cleared = InlineStaging.Clear(source, 42, null, origin: "net10.0");

        await Assert.That(cleared).IsEqualTo(1);
        // net8's trio is still there, and still reviewable
        await Assert.That(project.StagedFiles().Count).IsEqualTo(3);
    }

    /// <summary>
    /// What a test run stages for itself goes through InlinePatchFile.Write, and its patch names no
    /// framework: only the send to a queue owner ever stamped one. Every such trio was unlabeled,
    /// and an unlabeled trio is cleared whichever framework asks, so scoping a clear to an origin
    /// did nothing for the files a run with no viewer actually leaves.
    /// </summary>
    [Test]
    public async Task ATrioAProcessStagesIsLabelledWithItsFramework()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        var patchFile = project.Stage("ThisFramework", Patch(source, "content"));

        await Assert.That(InlinePatchFile.TryRead(patchFile, out var read)).IsTrue();
        await Assert.That(read!.Framework).IsEqualTo(RuntimeMoniker.Current);

        // Another framework of the same project, passing where this one failed
        var cleared = InlineStaging.Clear(source, 42, null, origin: "net0.0");

        await Assert.That(cleared).IsEqualTo(0);
        await Assert.That(File.Exists(patchFile)).IsTrue();
    }

    // A patch that says where it came from is staged as it says: a queue owner writing out what
    // other processes sent it is not the framework those snapshots belong to
    [Test]
    public async Task ATrioStagedWithAFrameworkKeepsIt()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        var patchFile = project.Stage("OtherFramework", Patch(source, "content", framework: "net0.0"));

        await Assert.That(InlinePatchFile.TryRead(patchFile, out var read)).IsTrue();
        await Assert.That(read!.Framework).IsEqualTo("net0.0");
    }

    /// <summary>
    /// The clear a passing run makes: its own framework's trio, and nobody else's. A caller has no
    /// way to name its framework as DiffEngine labels it, so the one clear it could make was for
    /// every framework, and the one that passed took the snapshot of the one still failing.
    /// </summary>
    [Test]
    public async Task SettleTakesThisFrameworksTrioAndLeavesAnothers()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        var mine = project.Stage("ThisFramework", Patch(source, "from this one"));
        var theirs = project.Stage("OtherFramework", Patch(source, "from another", framework: "net0.0"));

        var cleared = InlineStaging.Settle(source, 42, null);

        await Assert.That(cleared).IsEqualTo(1);
        await Assert.That(File.Exists(mine)).IsFalse();
        await Assert.That(File.Exists(theirs)).IsTrue();
    }

    // The member and the value narrow a settle on disk as they narrow a clear
    [Test]
    public async Task SettleFindsACallSiteWhoseLineHasMovedByMember()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        var mine = project.Stage("ThisFramework", Patch(source, "new", line: 42, member: "MyTest"));

        await Assert.That(InlineStaging.Settle(source, 807, "MyTest", value: "what a sibling holds")).IsEqualTo(0);
        await Assert.That(InlineStaging.Settle(source, 807, "MyTest", value: "new")).IsEqualTo(1);
        await Assert.That(File.Exists(mine)).IsFalse();
    }

    [Test]
    public async Task ClearFindsACallSiteWhoseLineHasMovedByMember()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        InlineStaging.Persist([new(Patch(source, "content", line: 42, member: "MyTest"))]);

        var cleared = InlineStaging.Clear(source, 807, "MyTest");

        await Assert.That(cleared).IsEqualTo(1);
        await Assert.That(project.StagedFiles()).IsEmpty();
    }

    /// <summary>
    /// The same rule on disk. The passing call's clear finds no trio at its own line and falls
    /// back to the member, which holds one staged call site - the failing sibling's. The value the
    /// passing call holds is neither that call site's anchor nor its new content, so it stays.
    /// </summary>
    [Test]
    public async Task ClearFromAPassingSiblingKeepsTheFailingSiblingsStagedTrio()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        InlineStaging.Persist([new(Patch(source, "new", framework: "net10.0", line: 20, member: "MyTest"))]);

        var cleared = InlineStaging.Clear(source, 10, "MyTest", origin: "net10.0", value: "what the sibling holds");

        await Assert.That(cleared).IsEqualTo(0);
        await Assert.That(project.StagedFiles().Count).IsEqualTo(3);
    }

    /// <summary>
    /// The queue's rule for an entry under the key, on disk. A passing call that an accept above
    /// it moved onto a line another test staged at is not that test's call site, and its clear
    /// used to take the trio for being at the line.
    /// </summary>
    [Test]
    public async Task ClearFromAnotherMemberAtATriosLineLeavesTheTrio()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        InlineStaging.Persist([new(Patch(source, "new", line: 42, member: "TestB"))]);

        var cleared = InlineStaging.Clear(source, 42, "TestA", value: "what the passing call holds");

        await Assert.That(cleared).IsEqualTo(0);
        await Assert.That(project.StagedFiles().Count).IsEqualTo(3);
    }

    // A test renamed since it staged: the line still names it, and the value says it settled
    [Test]
    public async Task ClearFromARenamedMemberTakesTheTrioItsValueSettles()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        InlineStaging.Persist([new(Patch(source, "new", line: 42, member: "OldName"))]);

        var cleared = InlineStaging.Clear(source, 42, "NewName", value: "new");

        await Assert.That(cleared).IsEqualTo(1);
        await Assert.That(project.StagedFiles()).IsEmpty();
    }

    [Test]
    public async Task ClearByMemberTakesACallSiteTheValueSettles()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        InlineStaging.Persist([new(Patch(source, "new", line: 42, member: "MyTest"))]);

        var cleared = InlineStaging.Clear(source, 807, "MyTest", value: "old");

        await Assert.That(cleared).IsEqualTo(1);
        await Assert.That(project.StagedFiles()).IsEmpty();
    }

    /// <summary>
    /// A member holding several inline snapshots cannot say which of them was settled, and
    /// deleting the wrong one discards a snapshot that is still pending.
    /// </summary>
    [Test]
    public async Task ClearLeavesAnAmbiguousMemberAlone()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        InlineStaging.Persist(
        [
            new(Patch(source, "first", line: 42, member: "MyTest")),
            new(Patch(source, "second", line: 48, member: "MyTest")),
        ]);

        var cleared = InlineStaging.Clear(source, 807, "MyTest");

        await Assert.That(cleared).IsEqualTo(0);
        await Assert.That(project.StagedFiles().Count).IsEqualTo(6);
    }

    /// <summary>
    /// The project a source belongs to is cached, since it cannot move while a run is going. The
    /// staging directories under it are not, and this is why: a run that finds no queue owner
    /// creates one as it goes, so it can appear after an earlier clear already looked and found
    /// nothing.
    /// </summary>
    [Test]
    public async Task ClearFindsStagingCreatedAfterAnEarlierLook()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");

        await Assert.That(InlineStaging.Clear(source, 42, null)).IsEqualTo(0);

        InlineStaging.Persist([new(Patch(source, "content"))]);

        await Assert.That(InlineStaging.Clear(source, 42, null)).IsEqualTo(1);
        await Assert.That(project.StagedFiles()).IsEmpty();
    }

    /// <summary>
    /// The same for what a test run stages for itself, which does not go through Persist. The walk
    /// for staging directories is kept between clears, and a write through InlinePatchFile is one
    /// of the two things that say it no longer stands.
    /// </summary>
    [Test]
    public async Task ClearFindsWhatThisProcessStagedAfterAnEarlierLook()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");

        await Assert.That(InlineStaging.Clear(source, 42, null)).IsEqualTo(0);

        var patchFile = project.Stage("ThisFramework", Patch(source, "content"));

        await Assert.That(InlineStaging.Clear(source, 42, null)).IsEqualTo(1);
        await Assert.That(File.Exists(patchFile)).IsFalse();
    }

    /// <summary>
    /// Another process can stage under the same obj, and nothing tells this one that it has: a
    /// second framework of the same run, or a queue owner writing its queue out as it exits. A
    /// directory it creates is found once the walk this process kept has had its life, which is
    /// why that life is short.
    /// </summary>
    [Test]
    public async Task ClearFindsWhatAnotherProcessStagedOnceTheLookIsStale()
    {
        var previous = InlineStaging.RecheckStagingAfter;
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        try
        {
            await Assert.That(InlineStaging.Clear(source, 42, null)).IsEqualTo(0);

            var patchFile = project.StageFromOutside("OtherProcess", Patch(source, "content"));

            // No life at all, which is every walk stale. Other tests may be clearing while this is
            // set, and all it changes for them is that they walk too
            InlineStaging.RecheckStagingAfter = TimeSpan.Zero;
            await Assert.That(InlineStaging.Clear(source, 42, null)).IsEqualTo(1);
            await Assert.That(File.Exists(patchFile)).IsFalse();
        }
        finally
        {
            InlineStaging.RecheckStagingAfter = previous;
        }
    }

    [Test]
    public async Task ClearLeavesAnotherSourceFileAlone()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        var other = project.Source("OtherTests.cs");
        InlineStaging.Persist([new(Patch(other, "content", member: "MyTest"))]);

        var cleared = InlineStaging.Clear(source, 42, "MyTest");

        await Assert.That(cleared).IsEqualTo(0);
        await Assert.That(project.StagedFiles().Count).IsEqualTo(3);
    }

    /// <summary>
    /// A test run stages under its own intermediate directory, which is normally inside the
    /// project's obj and found anyway, but does not have to be.
    /// </summary>
    [Test]
    public async Task ClearTakesAnExtraDirectory()
    {
        using var project = new TempProject();
        using var elsewhere = new TempProject();
        var source = project.Source("SampleTests.cs");

        var staging = Path.Combine(elsewhere.Root, InlineStaging.DirectoryName);
        Directory.CreateDirectory(staging);
        var patchFile = Path.Combine(staging, "Staged.inlinepatch");
        InlinePatchFile.Write(patchFile, Patch(source, "content"));

        var cleared = InlineStaging.Clear(source, 42, null, elsewhere.Root);

        await Assert.That(cleared).IsEqualTo(1);
        await Assert.That(File.Exists(patchFile)).IsFalse();
    }

    static InlinePatch Patch(
        string source,
        string content,
        string? framework = null,
        int line = 42,
        string? member = null) =>
        new(source, line, "\"old\"", content)
        {
            TestName = "SampleTests.Sample",
            OriginalValue = "old",
            Framework = framework,
            MemberName = member
        };

    // The name embeds an fnv1a of the call site so re-persisting overwrites; recomputed here so
    // the expected file name can be asserted exactly.
    static string Hash(string source)
    {
        var hash = 2166136261u;
        foreach (var character in $"{source}:42")
        {
            hash = (hash ^ character) * 16777619u;
        }

        return hash.ToString("x8");
    }

    // A directory shaped like a project: a project file at the top, a source file beside it, and
    // obj/VerifyInline expected to appear under it.
    sealed class TempProject : IDisposable
    {
        readonly string directory = Path.Combine(
            Path.GetTempPath(),
            $"inline-staging-{Guid.NewGuid():N}");

        public TempProject()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "Sample.csproj"), "<Project />");
        }

        // Named Root rather than Directory or Path, both of which are types this class uses.
        public string Root => directory;

        public string Source(string name)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, "// sample");
            return path;
        }

        /// <summary>
        /// Stages a patch the way a test run with no viewer does, through InlinePatchFile.Write,
        /// rather than the way an exiting owner does. Returns the patch file.
        /// </summary>
        public string Stage(string name, InlinePatch patch)
        {
            var path = Path.Combine(directory, "obj", InlineStaging.DirectoryName, $"{name}.inlinepatch");
            InlinePatchFile.Write(path, patch);
            return path;
        }

        /// <summary>
        /// The same file written with nothing of InlineStaging's or InlinePatchFile's involved in
        /// the write, which is all this process ever knows of what another one staged. In a
        /// framework's own intermediate directory, where a test run stages.
        /// </summary>
        public string StageFromOutside(string name, InlinePatch patch)
        {
            var staging = Path.Combine(directory, "obj", "Debug", "net0.0", InlineStaging.DirectoryName);
            Directory.CreateDirectory(staging);
            var path = Path.Combine(staging, $"{name}.inlinepatch");
            File.WriteAllText(path, InlinePatchFile.Build(patch));
            return path;
        }

        public IReadOnlyList<string> StagedFiles()
        {
            var staging = Path.Combine(directory, "obj", InlineStaging.DirectoryName);
            return Directory.Exists(staging)
                ? Directory.GetFiles(staging).OrderBy(_ => _, StringComparer.Ordinal).ToList()
                : [];
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Best effort cleanup of the temp directory.
            }
        }
    }

    /// <summary>
    /// A 242 character test name makes a 272 character received file name, past the 255 every
    /// file system here allows per component. The write throws IOException, TryPersist catches it,
    /// and the entry is reported as not written with nothing said anywhere.
    /// </summary>
    [Test]
    public async Task ALongTestNameIsStillPersisted()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"InlineStagingTests_long_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Sample.csproj"), "<Project />");
            var source = Path.Combine(directory, "SampleTests.cs");
            await File.WriteAllTextAsync(source, "// sample");

            var patch = new InlinePatch(source, 42, "\"old\"", "new")
            {
                TestName = $"SampleTests.{new string('a', 230)}",
                OriginalValue = "old",
                Framework = "net10.0"
            };

            var written = InlineStaging.Persist([new(patch)]);

            var staging = Path.Combine(directory, "obj", InlineStaging.DirectoryName);
            var files = Directory.Exists(staging) ? Directory.GetFiles(staging).Length : 0;
            await Assert.That(written).IsEqualTo(1);
            await Assert.That(files).IsEqualTo(3);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
