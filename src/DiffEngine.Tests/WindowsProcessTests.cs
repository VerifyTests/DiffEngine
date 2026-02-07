#if NET10_0
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
public class WindowsProcessTests
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
    [InlineData(@"notepad.exe C:\temp\file.txt")]
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

    [Fact]
    public void TryTerminateProcess_WithWindowedProcess_GracefullyCloses()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        // Start FakeDiffTool in windowed mode (has a main window)
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = FakeDiffTool.Exe,
            Arguments = "--windowed",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        Assert.NotNull(process);

        try
        {
            // Wait for the process to fully start and create its window
            Thread.Sleep(1000);

            // Attempt graceful termination via CloseMainWindow
            var result = WindowsProcess.TryTerminateProcess(process.Id);

            Assert.True(result);

            // Verify process exited gracefully
            Assert.True(process.WaitForExit(1000));
        }
        finally
        {
            // Cleanup: ensure process is killed if test fails
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public void TryTerminateProcess_WithNonWindowedProcess_ForcefullyTerminates()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        // Start FakeDiffTool - a console process without a main window
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = FakeDiffTool.Exe,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        Assert.NotNull(process);

        try
        {
            // Wait for the process to fully start
            Thread.Sleep(500);

            // Attempt termination (should fall back to forceful kill since no main window)
            var result = WindowsProcess.TryTerminateProcess(process.Id);

            Assert.True(result);

            // Verify process was terminated (should be immediate with forceful kill)
            Assert.True(process.WaitForExit(1000));
        }
        finally
        {
            // Cleanup: ensure process is killed if test fails
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public void TryTerminateProcess_WithInvalidProcessId_ReturnsFalse()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        // Use a very unlikely process ID
        var result = WindowsProcess.TryTerminateProcess(999999);

        Assert.False(result);
    }
}
