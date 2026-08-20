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

    static InlinePatch Patch(string source, string content, string? framework = null) =>
        new(source, 42, "\"old\"", content)
        {
            TestName = "SampleTests.Sample",
            OriginalValue = "old",
            Framework = framework
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

        public string Source(string name)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, "// sample");
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
}
