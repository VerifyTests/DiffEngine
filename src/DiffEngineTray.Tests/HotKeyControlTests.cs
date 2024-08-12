#if DEBUG
[TestFixture]
public class HotKeyControlTests
{
    [Test]
    public async Task WithKeys()
    {
        using var target = new HotKeyControl
        {
            HotKey = new()
            {
                Shift = true,
                Key = "A"
            }
        };
        await Verify(target);
    }

    [Test]
    public async Task Default()
    {
        using var target = new HotKeyControl();
        await Verify(target);
    }
}
#endif