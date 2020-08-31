using System.Threading.Tasks;
using DiffEngineTray.Controls;
using VerifyXunit;
using Xunit;
using Xunit.Abstractions;

[UsesVerify]
public class OptionsFormTests :
    XunitContextBase
{
    public OptionsFormTests(ITestOutputHelper output) :
        base(output)
    {
    }

    [Fact]
    public async Task Default()
    {
        await Verifier.Verify(new OptionsForm());
    }
}