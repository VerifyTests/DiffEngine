public class SolutionDirectoryFinderTests
{
#if DEBUG
    static string SourceFile { get; } = GetSourceFile();
    static string GetSourceFile([CallerFilePath] string path = "") => path;

    [Test]
    public Task Find() =>
        Verify(SolutionDirectoryFinder.Find(SourceFile));
#endif

    /// <summary>
    /// Paths arrive from another process and are not guaranteed to exist here. A directory that is
    /// not there is walked past rather than thrown over: the throw used to escape PiperServer,
    /// which dropped the pending move or delete and opened an issue page in the browser.
    /// </summary>
    [Test]
    public async Task MissingDirectoryIsWalkedPast()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiffEngineSlnFinder", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "App.sln"), "");

            // "Gone" was never created, and the solution above it still owns the file
            var result = SolutionDirectoryFinder.Find(Path.Combine(root, "Gone", "Sample.verified.txt"));

            await Assert.That(result).IsEqualTo("App");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Find has to answer for anything for its callers to stop guarding it, so a path this machine
    /// cannot use at all is ungrouped, the same as one with no solution above it.
    /// </summary>
    [Test]
    public async Task UnusablePathIsNotFound() =>
        await Assert.That(SolutionDirectoryFinder.Find("no\0such\\Sample.verified.txt")).IsNull();

    [Test]
    public async Task SiblingWithSharedPrefixIsNotMatched()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiffEngineSlnFinder", Guid.NewGuid().ToString("N"));
        var appDir = Path.Combine(root, "App");
        var appTestsDir = Path.Combine(root, "AppTests");
        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(appTestsDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(appDir, "App.sln"), "");
            await File.WriteAllTextAsync(Path.Combine(appTestsDir, "AppTests.sln"), "");

            // Resolve a file inside App first so its directory gets cached.
            var appResult = SolutionDirectoryFinder.Find(Path.Combine(appDir, "Class.cs"));
            await Assert.That(appResult).IsEqualTo("App");

            // A sibling that merely shares the "App" name prefix must resolve to its own
            // solution, not to the cached "App" directory.
            var testsResult = SolutionDirectoryFinder.Find(Path.Combine(appTestsDir, "Tests.cs"));
            await Assert.That(testsResult).IsEqualTo("AppTests");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
