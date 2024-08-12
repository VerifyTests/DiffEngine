#if DEBUG
public class HotKeyControlTests
{
    [Fact]
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

    [Fact]
    public async Task Default()
    {
        using var target = new HotKeyControl();
        await Verify(target);
    }
}
#endif