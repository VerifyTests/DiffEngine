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
    public async Task TryFind()
    {
        var found = SolutionDirectoryFinder.TryFind(SourceFile, out var path);
        await Verifier.Verify(new {found, path});
    }
}