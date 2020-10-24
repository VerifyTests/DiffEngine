using System.Collections.Generic;
using System.Threading.Tasks;
using DiffEngineTray.Common;
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
        VersionReader.VersionString = "TheVersion";
    }

    //[Fact]
    //[Trait("Category", "Integration")]
    //public void Launch()
    //{
    //    using var form = new OptionsForm(
    //        new Settings
    //        {
    //            AcceptAllHotKey = new HotKey
    //            {
    //                Shift = true,
    //                Key = "A"
    //            }
    //        },
    //        x => Task.FromResult<IReadOnlyList<string>>(new List<string>()));
    //    form.ShowDialog();
    //    form.BringToFront();
    //}
#if DEBUG
    [Fact]
    public async Task WithKeys()
    {
        using var form = new OptionsForm(
            new MockMessageBox(),
            new Settings
            {
                AcceptAllHotKey = new HotKey
                {
                    Shift = true,
                    Key = "A"
                }
            },
            x => Task.FromResult<IReadOnlyList<string>>(new List<string>()));
        await Verifier.Verify(form);
    }

    [Fact]
    public async Task Default()
    {
        using var form = new OptionsForm(
            new MockMessageBox(),
            new Settings(),
            x => Task.FromResult<IReadOnlyList<string>>(new List<string>()));
        await Verifier.Verify(form);
    }
#endif
}

public class MockMessageBox : IMessageBox
{
    public bool? Show(string message, string title, MessageBoxIcon icon, MessageBoxButtons buttons = MessageBoxButtons.YesNo)
    {
        return null;
    }
}