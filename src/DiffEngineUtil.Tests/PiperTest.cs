using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

public class PiperTest :
    XunitContextBase
{
    [Fact]
    public async Task Simple()
    {
        string? received = null;
        var source = new CancellationTokenSource();
        var task = Piper.Start(s => received = s, source.Token);
        await Piper.Send("Foo", source.Token);
        source.Cancel();
        await task;
        Assert.Equal("Foo", received);
    }

    public PiperTest(ITestOutputHelper output) :
        base(output)
    {
    }
}