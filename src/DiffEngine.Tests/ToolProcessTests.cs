using System.IO.Pipes;

/// <summary>
/// What a diff tool takes from the test host that starts it.
/// <para>
/// A test host's output goes to a pipe that <c>dotnet test</c> reads until every writer has closed
/// it. A tool started through ShellExecute inherits nothing, and most tools are declared that way.
/// The ones declared without it - the Word and Excel comparers, VS Code, Cursor - were handed every
/// inheritable handle the host had, the write end of that pipe among them. So a run that opened
/// one of them did not return until the process it had started was gone, and the Word comparer's
/// stays until Word is closed.
/// </para>
/// <para>
/// The real tools are never started here. A console program, a windowed one and a script stand in
/// for them: ping, FakeDiffTool, and a script that runs ping.
/// </para>
/// </summary>
[NotInParallel]
[RunOn(TUnit.Core.Enums.OS.Windows)]
public class ToolProcessTests
{
    /// <summary>
    /// The pipe is the one <c>dotnet test</c> reads a host's output from: its write end can be
    /// inherited, and reading it only ends once every copy of that end is closed. This process
    /// closes its own, so a read that does not end is a tool holding one.
    /// </summary>
    [Test]
    [Arguments("console")]
    [Arguments("windowed")]
    [Arguments("script")]
    public async Task AToolDeclaredWithoutShellExecuteInheritsNothing(string kind)
    {
        var (exe, arguments) = StandIn(kind);
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

        var processId = DiffRunner.LaunchProcess(Tool(exe), arguments);

        using var process = Process.GetProcessById(processId);
        var read = Task.Run(() => pipe.ReadByte());
        try
        {
            pipe.DisposeLocalCopyOfClientHandle();
            var ended = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(2))) == read;

            // Still running, or the pipe ending says nothing about what a running tool holds
            await Assert.That(process.HasExited).IsFalse();
            await Assert.That(ended).IsTrue();
        }
        finally
        {
            // Before the pipe is disposed, so a read that a tool is holding open ends first
            KillTree(processId);
            await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(10)));
        }
    }

    /// <summary>
    /// A tool is found again, to be refreshed or killed, by the command line it was started with:
    /// the quoted path and then the arguments, as a tool started any other way has.
    /// </summary>
    [Test]
    public async Task AToolDeclaredWithoutShellExecuteIsStillFoundByItsCommand()
    {
        var (exe, arguments) = StandIn("windowed");

        var processId = DiffRunner.LaunchProcess(Tool(exe), arguments);

        try
        {
            var command = ProcessCleanup.FindAll().Single(_ => _.Process == processId).Command;

            await Assert.That(command).IsEqualTo($"\"{exe}\" {arguments}");
        }
        finally
        {
            KillTree(processId);
        }
    }

    /// <summary>
    /// VS Code's launcher is a script in a folder with spaces in its name, and it is handed two
    /// quoted paths, which is the arrangement the command interpreter is known for mangling. The
    /// command line is the one that was always built, so each path arrives whole.
    /// </summary>
    [Test]
    public async Task AScriptInAFolderWithSpacesIsGivenItsArguments()
    {
        var (exe, arguments) = StandIn("script");
        var folder = Path.GetDirectoryName(exe)!;
        var written = Path.Combine(folder, "arguments.txt");
        File.Delete(written);

        var processId = DiffRunner.LaunchProcess(Tool(exe), arguments);

        try
        {
            for (var attempt = 0; attempt < 40 && !File.Exists(written); attempt++)
            {
                await Task.Delay(250);
            }

            // Once more, for a file that exists a moment before its line is in it
            await Task.Delay(250);
            await Assert.That((await File.ReadAllTextAsync(written)).Trim())
                .IsEqualTo($"{Path.Combine(folder, "Sample received.txt")}|{Path.Combine(folder, "Sample verified.txt")}");
        }
        finally
        {
            KillTree(processId);
        }
    }

    [Test]
    public async Task TheCommandLineIsTheQuotedPathThenTheArguments()
    {
        await Assert.That(WindowsProcess.CommandLine(@"C:\Program Files\Tool\tool.exe", "\"a.txt\" \"b.txt\""))
            .IsEqualTo("\"C:\\Program Files\\Tool\\tool.exe\" \"a.txt\" \"b.txt\"");
        // Nothing trailing, which would be an argument to some programs
        await Assert.That(WindowsProcess.CommandLine(@"C:\Tool\tool.exe", ""))
            .IsEqualTo("\"C:\\Tool\\tool.exe\"");
        await Assert.That(WindowsProcess.CommandLine("\"C:\\Tool\\tool.exe\"", "a"))
            .IsEqualTo("\"C:\\Tool\\tool.exe\" a");
    }

    /// <summary>
    /// A tool that cannot be started is still reported the way it was, with the command that
    /// failed, rather than as whatever Windows said about it.
    /// </summary>
    [Test]
    public async Task AToolThatCannotBeStartedSaysWhichOne()
    {
        var exe = Path.Combine(TempDirectory, "not-an-executable.exe");
        await File.WriteAllTextAsync(exe, "not an executable");

        var exception = Assert.Throws<Exception>(() => DiffRunner.LaunchProcess(Tool(exe), "\"a.txt\" \"b.txt\""));

        await Assert.That(exception.Message).Contains("Failed to launch diff tool.");
        await Assert.That(exception.Message).Contains(exe);
    }

    /// <summary>
    /// As the Word comparer is declared: no ShellExecute, and no window.
    /// </summary>
    static ResolvedTool Tool(string exe) =>
        new(
            name: "StandIn",
            exePath: exe,
            launchArguments: new(
                Left: (temp, target) => $"\"{target}\" \"{temp}\"",
                Right: (temp, target) => $"\"{temp}\" \"{target}\""),
            isMdi: false,
            autoRefresh: false,
            binaryExtensions: [],
            requiresTarget: false,
            supportsText: true,
            useShellExecute: false,
            createNoWindow: true);

    /// <summary>
    /// Something of each kind that outlives the two seconds a test waits, and the arguments that
    /// make it do so.
    /// </summary>
    static (string Exe, string Arguments) StandIn(string kind)
    {
        const string wait = "-n 8 127.0.0.1";
        var ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
        switch (kind)
        {
            case "console":
                return (ping, wait);
            case "windowed":
                // It sleeps for five seconds whatever it is given. Two paths, because that is the
                // shape of command line ProcessCleanup lists
                return (FakeDiffTool.Exe, $"\"{Path.Combine(TempDirectory, "Sample.received.txt")}\" \"{Path.Combine(TempDirectory, "Sample.verified.txt")}\"");
            case "script":
                // In a folder with a space in its name, as VS Code's launcher is, and it writes
                // down the two paths it was given before it waits
                var folder = Path.Combine(TempDirectory, "with a space");
                Directory.CreateDirectory(folder);
                var script = Path.Combine(folder, "stand-in.cmd");
                File.WriteAllText(script, $"@echo %~1^|%~2> \"%~dp0arguments.txt\"\r\n@\"{ping}\" {wait} > nul\r\n");
                return (script, $"\"{Path.Combine(folder, "Sample received.txt")}\" \"{Path.Combine(folder, "Sample verified.txt")}\"");
            default:
                throw new($"Unknown kind: {kind}");
        }
    }

    /// <summary>
    /// The process and whatever it started, since the script's wait is a child of its own.
    /// </summary>
    static void KillTree(int processId)
    {
        using var kill = Process.Start(
            new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), $"/PID {processId} /T /F")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            })!;
        kill.WaitForExit(10000);
    }

    static string TempDirectory { get; } = CreateDirectory();

    static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"DiffEngine.ToolProcess.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    [After(Class)]
    public static void DeleteDirectory()
    {
        try
        {
            Directory.Delete(TempDirectory, true);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            // A stand-in on its way out holds the script it was started from for a moment
        }
    }
}
