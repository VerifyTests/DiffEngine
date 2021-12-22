#if DEBUG
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