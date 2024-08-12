#if NET8_0
public class PiperTest
{
    [Test]
    public Task MoveJson() =>
        Verify(
            PiperClient.BuildMovePayload(
                "theTempFilePath",
                "theTargetFilePath",
                "theExePath",
                "TheArguments",
                true,
                1000));

    [Test]
    public Task DeleteJson() =>
        Verify(
            PiperClient.BuildMovePayload(
                "theTempFilePath",
                "theTargetFilePath",
                "theExePath",
                "TheArguments",
                true,
                1000));

    [Test]
    public async Task Delete()
    {
        DeletePayload received = null!;
        var source = new CancelSource();
        var task = PiperServer.Start(_ => { }, s => received = s, source.Token);
        await PiperClient.SendDeleteAsync("Foo", source.Token);
        await Task.Delay(1000, source.Token);
        source.Cancel();
        await task;
        await Verify(received);
    }

    [Test]
    public async Task Move()
    {
        MovePayload received = null!;
        var source = new CancelSource();
        var task = PiperServer.Start(s => received = s, _ => { }, source.Token);
        await PiperClient.SendMoveAsync("Foo", "Bar", "theExe", "TheArguments \"s\"", true, 10, source.Token);
        await Task.Delay(1000, source.Token);
        source.Cancel();
        await task;
        await Verify(received);
    }

    [Test]
    public async Task SendOnly()
    {
        var file = Path.GetFullPath("temp.txt");
        File.Delete(file);
        await File.WriteAllTextAsync(file, "a");
        try
        {
            await PiperClient.SendMoveAsync(file, file, "theExe", "TheArguments \"s\"", true, 10);
            await PiperClient.SendDeleteAsync(file);
        }
        catch (InvalidOperationException)
        {
        }

        //TODO
        // await Verify(Logs)
        //     .ScrubLinesContaining("temp.txt")
        //     //TODO: add "scrub source dir" to verify and remove the below
        //     .ScrubLinesContaining("PiperClient");
    }
}
#endif