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
        string[] received = null!;
        var source = new CancellationTokenSource();
        var task = PiperServer.Start(s => received = s, source.Token);
        await PiperClient.Send(new []{"Foo", "Bar"}, source.Token);
        source.Cancel();
        await task;
        Assert.NotNull(received);
        Assert.Equal("Foo", received[0]);
        Assert.Equal("Bar", received[1]);
    }

    public PiperTest(ITestOutputHelper output) :
        base(output)
    {
    }
}