#if NET10_0
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
public class WindowsProcessTests
{
    [Test]
    [Arguments("\"C:\\Program Files\\Beyond Compare 4\\BComp.exe\" C:\\temp\\file.1.txt C:\\temp\\file.2.txt", true)]
    [Arguments("notepad.exe C:\\Users\\test\\doc.1.txt C:\\Users\\test\\doc.2.txt", true)]
    [Arguments("\"C:\\diff\\tool.exe\" D:\\path\\to\\source.1.cs D:\\path\\to\\target.2.cs", true)]
    [Arguments("code.exe --diff file.a.b file.c.d", true)]
    [Arguments("app.exe path.with.dots path.more.dots", true)]
    public async Task MatchesPattern_WithTwoFilePaths_ReturnsTrue(string commandLine, bool expected)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        await Assert.That(WindowsProcess.MatchesPattern(commandLine)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("notepad.exe")]
    [Arguments(@"notepad.exe C:\temp\file.txt")]
    [Arguments("cmd.exe /c dir")]
    [Arguments("explorer.exe")]
    [Arguments("")]
    [Arguments("singleword")]
    [Arguments("app.exe onepath.with.dots")]
    public async Task MatchesPattern_WithoutTwoFilePaths_ReturnsFalse(string commandLine)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        await Assert.That(WindowsProcess.MatchesPattern(commandLine)).IsFalse();
    }

    [Test]
    public async Task FindAll_ReturnsProcessCommands()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var result = WindowsProcess.FindAll();
        await Assert.That(result).IsNotNull();
        foreach (var cmd in result)
        {
            Debug.WriteLine($"{cmd.Process}: {cmd.Command}");
        }
    }

    [Test]
    public async Task TryTerminateProcess_WithWindowedProcess_GracefullyCloses()
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

        await Assert.That(process).IsNotNull();

        try
        {
            // Wait for the process to fully start and create its window
            Thread.Sleep(1000);

            // Attempt graceful termination via CloseMainWindow
            var result = WindowsProcess.TryTerminateProcess(process.Id);

            await Assert.That(result).IsTrue();

            // Verify process exited gracefully
            await Assert.That(process.WaitForExit(1000)).IsTrue();
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

    [Test]
    public async Task TryTerminateProcess_WithNonWindowedProcess_ForcefullyTerminates()
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

        await Assert.That(process).IsNotNull();

        try
        {
            // Wait for the process to fully start
            Thread.Sleep(500);

            // Attempt termination (should fall back to forceful kill since no main window)
            var result = WindowsProcess.TryTerminateProcess(process.Id);

            await Assert.That(result).IsTrue();

            // Verify process was terminated (should be immediate with forceful kill)
            await Assert.That(process.WaitForExit(1000)).IsTrue();
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

    [Test]
    public async Task TryTerminateProcess_WithInvalidProcessId_ReturnsFalse()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        // Use a very unlikely process ID
        var result = WindowsProcess.TryTerminateProcess(999999);

        await Assert.That(result).IsFalse();
    }
}
