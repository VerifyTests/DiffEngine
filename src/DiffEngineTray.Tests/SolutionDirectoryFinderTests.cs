#if DEBUG
public class SolutionDirectoryFinderTests
{
    [Fact]
    public Task Find() =>
        Verify(SolutionDirectoryFinder.Find(Source.File()));
}
#endif