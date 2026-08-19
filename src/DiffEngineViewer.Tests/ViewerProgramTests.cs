/// <summary>
/// The exit-time half of the data-loss fix: an owning viewer writes what it still holds back to
/// the staging layout, an attached one leaves that to the owner it displays.
/// </summary>
public class ViewerProgramTests
{
    [Test]
    public async Task AnOwningViewerPersistsItsQueueOnExit()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        var state = Fixtures.Inline(Fixtures.Patch(source: source, framework: "net10.0"));

        var written = ViewerProgram.PersistOwned(state, link: null);

        await Assert.That(written).IsEqualTo(1);
        var staged = project.StagedFiles();
        await Assert.That(staged.Count).IsEqualTo(3);
        await Assert.That(staged.Count(_ => _.EndsWith(".inlinepatch"))).IsEqualTo(1);
    }

    [Test]
    public async Task AnAttachedViewerPersistsNothing()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        var state = Fixtures.Inline(Fixtures.Patch(source: source));

        // The link is what says this window displays someone else's queue. That owner is still
        // holding everything, so writing here would duplicate what is not lost.
        var link = new OwnerLink(new(state), port: 1);
        var written = ViewerProgram.PersistOwned(state, link);

        await Assert.That(written).IsEqualTo(0);
        await Assert.That(project.StagedFiles()).IsEmpty();
    }

    sealed class TempProject : IDisposable
    {
        readonly string directory = Path.Combine(
            Path.GetTempPath(),
            $"viewer-persist-{Guid.NewGuid():N}");

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
                ? Directory.GetFiles(staging)
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
