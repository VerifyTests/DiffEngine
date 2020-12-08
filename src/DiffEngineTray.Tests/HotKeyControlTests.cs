#if DEBUG
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using Xunit.Abstractions;

[UsesVerify]
public class HotKeyControlTests :
    XunitContextBase
{
    public HotKeyControlTests(ITestOutputHelper output) :
        base(output)
    {
    }

    [Fact]
    public async Task WithKeys()
    {
        using HotKeyControl target = new()
        {
            HotKey = new()
            {
                Shift = true,
                Key = "A"
            }
        };
        await Verifier.Verify(target);
    }

    [Fact]
    public async Task Default()
    {
        using HotKeyControl target = new();
        await Verifier.Verify(target);
    }
}
#endif