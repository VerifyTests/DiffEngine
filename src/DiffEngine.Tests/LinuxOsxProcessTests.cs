using System;
using Xunit;
using Xunit.Abstractions;

public class LinuxOsxProcessTests :
    XunitContextBase
{
    [Fact]
    public void TryParse()
    {
        var parse = LinuxOsxProcess.TryParse("309 Wed Aug 26 02:17:40 2020     /System/Library/coreauthd -foo", out var command);
        Assert.True(parse);
        var processCommand = command!.Value;
        Assert.Equal(309, processCommand.Process);
        Assert.Equal(new DateTime(2020,8,26,2,17,40), processCommand.StartTime);
        Assert.Equal("/System/Library/coreauthd -foo", processCommand.Command);
    }

    [Fact]
    public void TryParse_noSlash()
    {
        var parse = LinuxOsxProcess.TryParse("309 Wed Aug 26 02:17:40 2020     System/Library/coreauthd -foo", out var command);
        Assert.True(parse);
        var processCommand = command!.Value;
        Assert.Equal(309, processCommand.Process);
        Assert.Equal(new DateTime(2020,8,26,2,17,40), processCommand.StartTime);
        Assert.Equal("System/Library/coreauthd -foo", processCommand.Command);
    }

    [Fact]
    public void TryParse_singleDigit()
    {
        var parse = LinuxOsxProcess.TryParse("309 Thu Aug 6 02:17:40 2020     System/Library/coreauthd -foo", out var command);
        Assert.True(parse);
        var processCommand = command!.Value;
        Assert.Equal(309, processCommand.Process);
        Assert.Equal(new DateTime(2020,8,6,2,17,40), processCommand.StartTime);
        Assert.Equal("System/Library/coreauthd -foo", processCommand.Command);
    }

    public LinuxOsxProcessTests(ITestOutputHelper output) :
        base(output)
    {
    }
}