#if NET5_0_OR_GREATER
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
public class WindowsProcessTests(ITestOutputHelper output) :
    XunitContextBase(output)
{
    [Theory]
    [InlineData("\"C:\\Program Files\\Beyond Compare 4\\BComp.exe\" C:\\temp\\file.1.txt C:\\temp\\file.2.txt", true)]
    [InlineData("notepad.exe C:\\Users\\test\\doc.1.txt C:\\Users\\test\\doc.2.txt", true)]
    [InlineData("\"C:\\diff\\tool.exe\" D:\\path\\to\\source.1.cs D:\\path\\to\\target.2.cs", true)]
    [InlineData("code.exe --diff file.a.b file.c.d", true)]
    [InlineData("app.exe path.with.dots path.more.dots", true)]
    public void MatchesPattern_WithTwoFilePaths_ReturnsTrue(string commandLine, bool expected)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        Assert.Equal(expected, WindowsProcess.MatchesPattern(commandLine));
    }

    [Theory]
    [InlineData("notepad.exe")]
    [InlineData("notepad.exe C:\\temp\\file.txt")]
    [InlineData("cmd.exe /c dir")]
    [InlineData("explorer.exe")]
    [InlineData("")]
    [InlineData("singleword")]
    [InlineData("app.exe onepath.with.dots")]
    public void MatchesPattern_WithoutTwoFilePaths_ReturnsFalse(string commandLine)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        Assert.False(WindowsProcess.MatchesPattern(commandLine));
    }

    [Fact]
    public void FindAll_ReturnsProcessCommands()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var result = WindowsProcess.FindAll();
        Assert.NotNull(result);
        foreach (var cmd in result)
        {
            Debug.WriteLine($"{cmd.Process}: {cmd.Command}");
        }
    }
}
