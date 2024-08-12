[TestFixture]
public class LinuxOsxProcessTests
{
    [Test]
    public void TryParseWithZshInstalled()
    {
        var parse = LinuxOsxProcess.TryParse("20872 -zsh", out var command);
        True(parse);
        var processCommand = command!.Value;
        AreEqual(20872, processCommand.Process);
        AreEqual("-zsh", processCommand.Command);
    }

    [Test]
    public void TryParse()
    {
        var parse = LinuxOsxProcess.TryParse("309 /System/Library/coreauthd -foo", out var command);
        True(parse);
        var processCommand = command!.Value;
        AreEqual(309, processCommand.Process);
        AreEqual("/System/Library/coreauthd -foo", processCommand.Command);
    }

    [Test]
    public void TryParse_noSlash()
    {
        var parse = LinuxOsxProcess.TryParse("309 System/Library/coreauthd -foo", out var command);
        True(parse);
        var processCommand = command!.Value;
        AreEqual(309, processCommand.Process);
        AreEqual("System/Library/coreauthd -foo", processCommand.Command);
    }

    [Test]
    public void TryParse_singleDigit()
    {
        var parse = LinuxOsxProcess.TryParse("309 System/Library/coreauthd -foo", out var command);
        True(parse);
        var processCommand = command!.Value;
        AreEqual(309, processCommand.Process);
        AreEqual("System/Library/coreauthd -foo", processCommand.Command);
    }
}