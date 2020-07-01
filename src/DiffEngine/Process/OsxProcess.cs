using System.Collections.Generic;
using System.Diagnostics;
using System;
using System.IO;
using System.Text;
using DiffEngine;

static class OsxProcess
{
    public static bool TryTerminateProcess(ProcessCommand processCommand)
    {
        return false;
    }

    public static IEnumerable<ProcessCommand> FindAll()
    {
        var processList = Run();
        using StringReader reader = new StringReader(processList);
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

    public static string Run()
    {
        var errorBuilder = new StringBuilder();
        var outputBuilder = new StringBuilder();
        var arguments = "-o pid,command -x";
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
