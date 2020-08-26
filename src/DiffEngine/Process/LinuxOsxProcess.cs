using System.Collections.Generic;
using System.Diagnostics;
using System;
using System.IO;
using System.Text;
using DiffEngine;

static class LinuxOsxProcess
{
    //https://www.man7.org/linux/man-pages/man1/ps.1.html
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
            var timeoutError = $@"Process timed out. Command line: kill {processId}.";
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
            var trim = line.Trim();
            var indexOf = trim.IndexOf(' ');
            if (indexOf < 1)
            {
                continue;
            }
            var pidString = trim.Substring(0, indexOf);
            var pid = int.Parse(pidString);
            var command = trim.Substring(indexOf + 1);
            yield return new ProcessCommand(command, in pid);
        }
    }

    static string RunPs()
    {
        var errorBuilder = new StringBuilder();
        var outputBuilder = new StringBuilder();
        const string? arguments = "-o pid,command -x";
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
