public class LinuxOsxProcessTests
{
    [Test]
    public async Task TryParseWithZshInstalled()
    {
        var parse = LinuxOsxProcess.TryParse("20872 -zsh", out var command);
        await Assert.That(parse).IsTrue();
        var processCommand = command!.Value;
        await Assert.That(processCommand.Process).IsEqualTo(20872);
        await Assert.That(processCommand.Command).IsEqualTo("-zsh");
    }

    [Test]
    public async Task TryParse()
    {
        var parse = LinuxOsxProcess.TryParse("309 /System/Library/coreauthd -foo", out var command);
        await Assert.That(parse).IsTrue();
        var processCommand = command!.Value;
        await Assert.That(processCommand.Process).IsEqualTo(309);
        await Assert.That(processCommand.Command).IsEqualTo("/System/Library/coreauthd -foo");
    }

    [Test]
    public async Task TryParse_noSlash()
    {
        var parse = LinuxOsxProcess.TryParse("309 System/Library/coreauthd -foo", out var command);
        await Assert.That(parse).IsTrue();
        var processCommand = command!.Value;
        await Assert.That(processCommand.Process).IsEqualTo(309);
        await Assert.That(processCommand.Command).IsEqualTo("System/Library/coreauthd -foo");
    }

    [Test]
    public async Task TryParse_singleDigit()
    {
        var parse = LinuxOsxProcess.TryParse("309 System/Library/coreauthd -foo", out var command);
        await Assert.That(parse).IsTrue();
        var processCommand = command!.Value;
        await Assert.That(processCommand.Process).IsEqualTo(309);
        await Assert.That(processCommand.Command).IsEqualTo("System/Library/coreauthd -foo");
    }
}
