#if DEBUG
[UsesVerify]
public class SolutionDirectoryFinderTests(ITestOutputHelper output) :
    XunitContextBase(output)
{
    [Fact]
    public Task Find() =>
        Verify(SolutionDirectoryFinder.Find(SourceFile));
}
#endif