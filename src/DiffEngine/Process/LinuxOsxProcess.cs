using DiffEngine;

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
                CreateNoWindow = false,
            }
        };
        process.Start();

        if (process.DoubleWaitForExit())
        {
            return process.ExitCode == 0;
        }

        var message = $"Process timed out. Command line: kill {processId}.";
        throw new(message);
    }

    public static IEnumerable<ProcessCommand> FindAll()
    {
        var processList = RunPs();
        using var reader = new StringReader(processList);
        string? line;
        reader.ReadLine();
        while ((line = reader.ReadLine()) != null)
        {
            if (!TryParse(line, out var processCommand))
            {
                continue;
            }

            yield return processCommand!.Value;
        }
    }

    public static bool TryParse(string line, out ProcessCommand? processCommand)
    {
        try
        {
            var trim = line.Trim();
            var firstSpace = trim.IndexOf(' ');
            if (firstSpace < 1)
            {
                processCommand = null;
                return false;
            }

            var pidString = trim[..firstSpace];
            var pid = int.Parse(pidString);

            var timeAndCommandString = trim[(firstSpace + 1)..];
            var multiSpaceIndex = 0;
            string command;

            if (timeAndCommandString.IndexOf("   ", StringComparison.InvariantCulture) > 0)
            {
                multiSpaceIndex = timeAndCommandString.IndexOf("   ", firstSpace, StringComparison.InvariantCulture);
                command = timeAndCommandString[(multiSpaceIndex + 1)..].Trim();
            }
            else
            {
                command = timeAndCommandString[multiSpaceIndex..].Trim();
            }

            processCommand = new(command, in pid);
            return true;
        }
        catch (Exception exception)
        {
            throw new($"Could not parse command: {line}", exception);
        }
    }

    static string RunPs()
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
                CreateNoWindow = false,
            }
        };
        process.Start();
        process.OutputDataReceived += (_, args) => { outputBuilder.AppendLine(args.Data); };
        process.BeginOutputReadLine();
        process.ErrorDataReceived += (_, args) => { errorBuilder.AppendLine(args.Data); };
        process.BeginErrorReadLine();
        if (!process.DoubleWaitForExit())
        {
            var timeoutError = $@"Process timed out. Command line: ps {arguments}.
Output: {outputBuilder}
Error: {errorBuilder}";
            throw new(timeoutError);
        }

        if (process.ExitCode == 0)
        {
            return outputBuilder.ToString();
        }

        var error = $@"Could not execute process. Command line: ps {arguments}.
Output: {outputBuilder}
Error: {errorBuilder}";
        throw new(error);
    }

    //To work around https://github.com/dotnet/runtime/issues/27128
    static bool DoubleWaitForExit(this Process process)
    {
        var result = process.WaitForExit(500);
        if (result)
        {
            process.WaitForExit();
        }

        return result;
    }
}