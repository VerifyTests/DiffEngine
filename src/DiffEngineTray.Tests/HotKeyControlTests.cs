#if DEBUG
public class HotKeyControlTests
{
    [Test]
    public async Task WithKeys()
    {
        using var target = new HotKeyControl();
        target.ShowHotKey(
            new()
            {
                Shift = true,
                Key = "A"
            });
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
