#if DEBUG
[TestFixture]
public class SolutionDirectoryFinderTests
{
    [Test]
    public Task Find() =>
        Verify(SolutionDirectoryFinder.Find(Source.File()));
}
#endif