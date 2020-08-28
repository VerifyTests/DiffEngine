using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VerifyTests;
using VerifyXunit;
using Xunit;
using Xunit.Abstractions;

[UsesVerify]
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
        await Verifier.Verify(received);
    }

    [Fact]
    public async Task Move()
    {
        MovePayload received = null!;
        var source = new CancellationTokenSource();
        var task = PiperServer.Start(s => received = s, s => { }, source.Token);
        var processStartTime = Process.GetCurrentProcess().StartTime;
        await PiperClient.SendMove("Foo", "Bar", "theExe", "TheArguments", true, 10, processStartTime, source.Token);
        await Task.Delay(1000);
        source.Cancel();
        await task;
        await Verifier.Verify(received);
    }

    [Fact]
    public async Task SendOnly()
    {
        var file = Path.GetFullPath("temp.txt");
        File.Delete(file);
        await File.WriteAllTextAsync(file, "a");
        try
        {
            await PiperClient.SendMove(file, file,"theExe", "TheArguments", true, 10, null);
            await PiperClient.SendDelete(file);
        }
        catch (InvalidOperationException)
        {
        }

        var settings = new VerifySettings();
        settings.ScrubLinesContaining("temp.txt");
        //TODO: add "scrub source dir" to verify and remove the below
        settings.ScrubLinesContaining("PiperClient");
        await Verifier.Verify(Logs, settings);
    }

    public PiperTest(ITestOutputHelper output) :
        base(output)
    {
    }
}