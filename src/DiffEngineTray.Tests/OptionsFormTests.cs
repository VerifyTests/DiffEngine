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
        using var form = new OptionsForm
        {
            Settings = new Settings
            {
                HotKey = new HotKey
                {
                    Shift = true,
                    Key = "A"
                }
            }
        };
        form.ShowDialog();
        form.BringToFront();
    }

    [Fact]
    public async Task WithKeys()
    {
        using var form = new OptionsForm
        {
            Settings = new Settings
            {
                HotKey = new HotKey
                {
                    Shift = true,
                    Key = "A"
                }
            }
        };
        await Verifier.Verify(form);
    }

    [Fact]
    public async Task Default()
    {
        using var form = new OptionsForm
        {
            Settings = new Settings()
        };
        await Verifier.Verify(form);
    }
}