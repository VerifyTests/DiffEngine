#if DEBUG
public class SolutionDirectoryFinderTests
{
    static string SourceFile { get; } = GetSourceFile();
    static string GetSourceFile([CallerFilePath] string path = "") => path;

    [Test]
    public Task Find() =>
        Verify(SolutionDirectoryFinder.Find(SourceFile));
}
#endif
