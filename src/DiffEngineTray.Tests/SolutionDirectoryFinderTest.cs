using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using Xunit.Abstractions;

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
        return Verifier.Verify(SolutionDirectoryFinder.Find(SourceFile));
    }
}