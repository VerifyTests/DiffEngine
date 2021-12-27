#if DEBUG
[UsesVerify]
public class SolutionDirectoryFinderTests :
    XunitContextBase
{
    public SolutionDirectoryFinderTests(ITestOutputHelper output) :
        base(output)
    {
    }

    [Fact]
    public Task Find()
    {
        return Verify(SolutionDirectoryFinder.Find(SourceFile));
    }
}
#endif