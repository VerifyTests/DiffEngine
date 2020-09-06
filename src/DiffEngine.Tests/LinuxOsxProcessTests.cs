using Xunit;
using Xunit.Abstractions;

public class LinuxOsxProcessTests :
    XunitContextBase
{
    [Fact]
    public void TryParse()
    {
        var parse = LinuxOsxProcess.TryParse("309 /System/Library/coreauthd -foo", out var command);
        Assert.True(parse);
        var processCommand = command!.Value;
        Assert.Equal(309, processCommand.Process);
        Assert.Equal("/System/Library/coreauthd -foo", processCommand.Command);
    }

    [Fact]
    public void TryParse_noSlash()
    {
        var parse = LinuxOsxProcess.TryParse("309 System/Library/coreauthd -foo", out var command);
        Assert.True(parse);
        var processCommand = command!.Value;
        Assert.Equal(309, processCommand.Process);
        Assert.Equal("System/Library/coreauthd -foo", processCommand.Command);
    }

    [Fact]
    public void TryParse_singleDigit()
    {
        var parse = LinuxOsxProcess.TryParse("309 System/Library/coreauthd -foo", out var command);
        Assert.True(parse);
        var processCommand = command!.Value;
        Assert.Equal(309, processCommand.Process);
        Assert.Equal("System/Library/coreauthd -foo", processCommand.Command);
    }

    public LinuxOsxProcessTests(ITestOutputHelper output) :
        base(output)
    {
    }
}