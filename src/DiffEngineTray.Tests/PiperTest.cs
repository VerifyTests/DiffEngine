using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

public class PiperTest :
    XunitContextBase
{
    [Fact]
    public async Task Delete()
    {
        DeletePayload received = null!;
        var source = new CancellationTokenSource();
        var task = PiperServer.Start(s => { }, s => received = s, source.Token);
        await PiperClient.SendDelete("Foo", source.Token);
        await Task.Delay(1000);
        source.Cancel();
        await task;
        Assert.NotNull(received);
        Assert.Equal("Foo", received.File);
    }

    [Fact]
    public async Task Move()
    {
        MovePayload received = null!;
        var source = new CancellationTokenSource();
        var task = PiperServer.Start(s => received = s, s => { }, source.Token);
        var processStartTime = Process.GetCurrentProcess().StartTime;
        await PiperClient.SendMove("Foo", "Bar", true, 10, processStartTime, source.Token);
        await Task.Delay(1000);
        source.Cancel();
        await task;
        Assert.NotNull(received);
        Assert.Equal("Foo", received.Temp);
        Assert.Equal(processStartTime, received.ProcessStartTime);
        Assert.Equal("Bar", received.Target);
        Assert.True(received.CanKill);
        Assert.Equal(10, received.ProcessId);
    }

    [Fact]
    public async Task SendOnly()
    {
        var file = Path.GetFullPath("temp.txt");
        File.Delete(file);
        await File.WriteAllTextAsync(file, "a");
        try
        {
            await PiperClient.SendMove(file, file, true, 10, null);
        }
        catch (InvalidOperationException)
        {
        }
    }

    public PiperTest(ITestOutputHelper output) :
        base(output)
    {
    }
}