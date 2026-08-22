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

    /// <summary>
    /// A command with a run of three spaces in it. The removed branch went looking for exactly
    /// that and truncated the command to whatever followed it.
    /// </summary>
    [Test]
    public async Task TryParse_commandContainingRunsOfSpaces()
    {
        var parse = LinuxOsxProcess.TryParse("123 /usr/bin/tool   file.txt", out var command);
        await Assert.That(parse).IsTrue();
        var processCommand = command!.Value;
        await Assert.That(processCommand.Process).IsEqualTo(123);
        await Assert.That(processCommand.Command).IsEqualTo("/usr/bin/tool   file.txt");
    }

    /// <summary>
    /// A PID with more digits than the command has characters. The removed branch sliced by the
    /// PID's digit count, which is not an index into this string at all, so this threw
    /// ArgumentOutOfRangeException - and did so out of ProcessCleanup's static constructor, which
    /// makes it permanent for the process.
    /// </summary>
    [Test]
    public async Task TryParse_longPidShortCommand()
    {
        var parse = LinuxOsxProcess.TryParse("1234567 /x   y", out var command);
        await Assert.That(parse).IsTrue();
        var processCommand = command!.Value;
        await Assert.That(processCommand.Process).IsEqualTo(1234567);
        await Assert.That(processCommand.Command).IsEqualTo("/x   y");
    }
}
