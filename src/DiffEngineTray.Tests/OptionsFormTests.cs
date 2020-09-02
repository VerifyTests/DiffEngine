using System.Linq;
using System.Threading.Tasks;
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
    [Trait("Category", "Integration")]
    public void Launch()
    {
        using var form = new OptionsForm(
            new Settings
            {
                AcceptAllHotKey = new HotKey
                {
                    Shift = true,
                    Key = "A"
                }
            },
            () => Task.FromResult(Enumerable.Empty<string>()));
        form.ShowDialog();
        form.BringToFront();
    }
#if DEBUG
    [Fact]
    public async Task WithKeys()
    {
        using var form = new OptionsForm(
            new Settings
            {
                AcceptAllHotKey = new HotKey
                {
                    Shift = true,
                    Key = "A"
                }
            },
            () => Task.FromResult(Enumerable.Empty<string>()));
        await Verifier.Verify(form);
    }

    [Fact]
    public async Task Default()
    {
        using var form = new OptionsForm(
            new Settings(),
            () => Task.FromResult(Enumerable.Empty<string>()));
        await Verifier.Verify(form);
    }
#endif
}