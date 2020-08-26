using System.Collections.Generic;
using System.Diagnostics;
using System;
using System.IO;
using System.Text;
using DiffEngine;

static class LinuxOsxProcess
{
    public static bool TryTerminateProcess(int processId)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "kill",
                Arguments = processId.ToString(),
                UseShellExecute = false,
                CreateNoWindow = false,
            }
        };
        process.Start();
        if (!process.DoubleWaitForExit())
        {
            var timeoutError = $"Process timed out. Command line: kill {processId}.";
            throw new Exception(timeoutError);
        }

        return process.ExitCode == 0;
    }

    public static IEnumerable<ProcessCommand> FindAll()
    {
        var processList = RunPs();
        using var reader = new StringReader(processList);
        string line;
        reader.ReadLine();
        while ((line = reader.ReadLine()) != null)
        {
            if (TryParse(line, out var processCommand))
            {
                continue;
            }
            yield return processCommand!.Value;
        }
    }

    static bool TryParse(string line, out ProcessCommand? processCommand)
    {
        try
        {
            var trim = line.Trim();
            var firstSpace = trim.IndexOf(' ');
            if (firstSpace < 1)
            {
                processCommand = null;
                return true;
            }

            var pidString = trim.Substring(0, firstSpace);
            var pid = int.Parse(pidString);


            var secondSpace = trim.IndexOf(' ', firstSpace);
            var startTimeString = trim.Substring(firstSpace, secondSpace);
            var startTime = DateTime.Parse(startTimeString);

            var command = trim.Substring(secondSpace + 1);

            processCommand = new ProcessCommand(command, in pid, in startTime);
            return false;
        }
        catch (Exception exception)
        {
            throw new Exception($"Could not parser command: {line}", exception);
        }
    }

    static string RunPs()
    {
        var errorBuilder = new StringBuilder();
        var outputBuilder = new StringBuilder();
        const string? arguments = "-o pid,lstart,command -x";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
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
        process.OutputDataReceived += (sender, args) => { outputBuilder.AppendLine(args.Data); };
        process.BeginOutputReadLine();
        process.ErrorDataReceived += (sender, args) => { errorBuilder.AppendLine(args.Data); };
        process.BeginErrorReadLine();
        if (!process.DoubleWaitForExit())
        {
            var timeoutError = $@"Process timed out. Command line: ps {arguments}.
Output: {outputBuilder}
Error: {errorBuilder}";
            throw new Exception(timeoutError);
        }
        if (process.ExitCode == 0)
        {
            return outputBuilder.ToString();
        }

        var error = $@"Could not execute process. Command line: ps {arguments}.
Output: {outputBuilder}
Error: {errorBuilder}";
        throw new Exception(error);
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
