static class LinuxOsxProcess
{
    //https://www.man7.org/linux/man-pages/man1/ps.1.html
    public static bool TryTerminateProcess(int processId)
    {
        using var process = new Process
        {
            StartInfo = new()
            {
                FileName = "kill",
                Arguments = processId.ToString(),
                UseShellExecute = false,
                CreateNoWindow = false
            }
        };
        process.Start();

        if (process.DoubleWaitForExit())
        {
            return process.ExitCode == 0;
        }

        throw new($"Process timed out. Command line: kill {processId}.");
    }

    // candidateExeNames is accepted for signature parity with the Windows implementation but
    // ignored: a single `ps` invocation already returns every command line, so there is no
    // per-process syscall cost to avoid by filtering here.
#pragma warning disable IDE0060
    public static List<ProcessCommand> FindAll(HashSet<string>? candidateExeNames = null)
#pragma warning restore IDE0060
    {
        if (!TryRunPs(out var processList))
        {
            return [];
        }

        var commands = new List<ProcessCommand>();
        using var reader = new StringReader(processList);
        reader.ReadLine();
        while (reader.ReadLine() is { } line)
        {
            if (!TryParse(line, out var processCommand))
            {
                continue;
            }

            commands.Add(processCommand!.Value);
        }

        return commands;
    }

    public static bool TryParse(string line, out ProcessCommand? processCommand)
    {
        try
        {
            var trim = line.AsSpan().Trim();
            var firstSpace = trim.IndexOf(' ');
            if (firstSpace < 1)
            {
                processCommand = null;
                return false;
            }

            var pidString = trim[..firstSpace];
            var pid = int.Parse(pidString.ToString());

            // `ps -o pid,command` has exactly one separator, so everything after the first space
            // is the command. There used to be a second branch here looking for a run of three
            // spaces, left over from a format that also carried TIME, and it was wrong twice over:
            // it sliced by firstSpace, which is the PID's digit count and means nothing in this
            // string, and then applied the index it found to the unsliced span. So a command
            // containing three spaces was truncated, and a seven digit PID with a short command
            // threw ArgumentOutOfRangeException - out of ProcessCleanup's static constructor,
            // which makes it permanent for the process
            var command = trim[(firstSpace + 1)..].Trim();

            processCommand = new(command.ToString(), in pid);
            return true;
        }
        catch (Exception exception)
        {
            throw new($"Could not parse command: {line}", exception);
        }
    }

    static bool TryRunPs([NotNullWhen(true)] out string? result)
    {
        var errorBuilder = new StringBuilder();
        var outputBuilder = new StringBuilder();
        const string? arguments = "-o pid,command -x";
        using var process = new Process
        {
            StartInfo = new()
            {
                FileName = "ps",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = false
            }
        };
        try
        {
            process.Start();
        }
        catch (Exception exception)
            when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No ps on this machine. A minimal container without procps is the ordinary case, and
            // one that does not set DOTNET_RUNNING_IN_CONTAINER gets this far. Degrading to "no
            // running processes" is what the timeout below already does, and the alternative is
            // far worse than a wrong answer: this runs from ProcessCleanup's static constructor,
            // so it becomes a TypeInitializationException on every launch and kill for the life
            // of the process
            Trace.WriteLine($"DiffEngine: Could not start ps. Treating as no running processes. {exception.Message}");
            result = null;
            return false;
        }

        process.OutputDataReceived += (_, args) =>
        {
            outputBuilder.AppendLine(args.Data);
        };
        process.BeginOutputReadLine();
        process.ErrorDataReceived += (_, args) =>
        {
            errorBuilder.AppendLine(args.Data);
        };
        process.BeginErrorReadLine();
        if (!process.DoubleWaitForExit())
        {
            Trace.WriteLine($"DiffEngine: Process timed out. Command line: ps {arguments}");
            result = null;
            return false;
        }

        if (process.ExitCode != 0)
        {
            // Reported rather than thrown, for the same reason a failure to start is: the caller
            // is a static constructor, and a throw there is permanent for the process
            Trace.WriteLine(
                $"""
                 DiffEngine: ps exited with {process.ExitCode}. Treating as no running processes. Command line: ps {arguments}.
                 Output: {outputBuilder}
                 Error: {errorBuilder}
                 """);
            result = null;
            return false;
        }

        result = outputBuilder.ToString();
        return true;
    }

    //To work around https://github.com/dotnet/runtime/issues/27128
    static bool DoubleWaitForExit(this Process process)
    {
        var result = process.WaitForExit(1000);
        if (result)
        {
            process.WaitForExit();
        }

        return result;
    }
}