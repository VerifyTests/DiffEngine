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
        Payload received = null!;
        var source = new CancellationTokenSource();
        var task = PiperServer.Start(s => received = s, source.Token);
        await PiperClient.Send("Foo", "Bar",true,true,10, source.Token);
        source.Cancel();
        await task;
        Assert.NotNull(received);
        Assert.Equal("Foo", received.Temp);
        Assert.Equal("Bar", received.Target);
        Assert.True(received.IsMdi);
        Assert.True(received.AutoRefresh);
        Assert.Equal(10, received.ProcessId);
    }

    public PiperTest(ITestOutputHelper output) :
        base(output)
    {
    }
}