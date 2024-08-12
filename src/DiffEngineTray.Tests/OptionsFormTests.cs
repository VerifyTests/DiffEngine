#if DEBUG
public class OptionsFormTests
{
    public OptionsFormTests() =>
        VersionReader.VersionString = "TheVersion";

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

    [Test]
    public async Task WithKeys()
    {
        using var form = new OptionsForm(
            new()
            {
                AcceptAllHotKey = new()
                {
                    Shift = true,
                    Key = "A"
                }
            },
            _ => Task.FromResult<IReadOnlyCollection<string>>(new List<string>()));
        await Verify(form);
    }

    [Test]
    public async Task Default()
    {
        using var form = new OptionsForm(
            new(),
            _ => Task.FromResult<IReadOnlyCollection<string>>(new List<string>()));
        await Verify(form);
    }
}
#endif