public class SolutionDirectoryFinderTests
{
#if DEBUG
    static string SourceFile { get; } = GetSourceFile();
    static string GetSourceFile([CallerFilePath] string path = "") => path;

    [Test]
    public Task Find() =>
        Verify(SolutionDirectoryFinder.Find(SourceFile));
#endif

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
