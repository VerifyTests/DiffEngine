#if DEBUG
public class SolutionDirectoryFinderTests
{
    static string SourceFile { get; } = GetSourceFile();
    static string GetSourceFile([CallerFilePath] string path = "") => path;

    [Fact]
    public Task Find() =>
        Verify(SolutionDirectoryFinder.Find(SourceFile));
}
#endif
