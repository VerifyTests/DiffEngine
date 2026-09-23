namespace DiffEngine;

public static class ProcessCleanup
{
    static List<ProcessCommand> commands;
    static readonly object gate = new();
    static Func<HashSet<string>?, List<ProcessCommand>> findAll;
    static Func<int, bool> tryTerminateProcess;

    static ProcessCleanup()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            findAll = WindowsProcess.FindAll;
            tryTerminateProcess = WindowsProcess.TryTerminateProcess;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                 RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            findAll = LinuxOsxProcess.FindAll;
            tryTerminateProcess = LinuxOsxProcess.TryTerminateProcess;
        }
        else
        {
            throw new("Unknown OS");
        }

        Refresh();
    }

    public static IReadOnlyCollection<ProcessCommand> Commands => commands;

    [MemberNotNull(nameof(commands))]
    public static void Refresh() =>
        // Only processes launched as a resolved diff tool can ever match a command DiffEngine
        // builds, so restrict the (expensive on Windows) per-process command-line reads to those
        // images instead of scanning every process on the machine.
        commands = findAll(CandidateExeNames())
            .OrderByDescending(_ => _.Process)
            .ToList();

    static HashSet<string> CandidateExeNames()
    {
        HashSet<string> names = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (var tool in DiffTools.Resolved)
        {
            names.Add(Path.GetFileName(tool.ExePath));
        }

        return names;
    }

    /// <summary>
    /// Find a process with the matching command line and kill it.
    /// </summary>
    public static void Kill(string command)
    {
        Guard.AgainstEmpty(command, nameof(command));
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            command = TrimCommand(command);
        }

        var matchingCommands = Commands
            .Where(_ => _.Command == command)
            .Where(StillRunning)
            .ToList();
        Logging.Write($"Kill: {command}. Matching count: {matchingCommands.Count}");
        if (matchingCommands.Count == 0)
        {
            var separator = Environment.NewLine + "\t";
            var joined = string.Join(separator, Commands.Select(_ => _.Command));
            Logging.Write($"No matching commands. All commands: {separator}{joined}.");
            return;
        }

        foreach (var processCommand in matchingCommands)
        {
            TerminateProcessIfExists(processCommand.Process);
        }
    }

    static string TrimCommand(string command) =>
        command.Replace("\"", "");

    public static bool IsRunning(string command) =>
        TryGetProcessInfo(command, out _);

    public static bool TryGetProcessInfo(string command, out ProcessCommand process)
    {
        Guard.AgainstEmpty(command, nameof(command));
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            command = TrimCommand(command);
        }

        process = commands.FirstOrDefault(_ => _.Command == command);
        if (process.Equals(default(ProcessCommand)))
        {
            return false;
        }

        if (StillRunning(process))
        {
            return true;
        }

        Forget(process);
        process = default;
        return false;
    }

    /// <summary>
    /// A tool this process started, so a relaunch or a kill later in the same run finds it. The
    /// list is otherwise taken once, when the type initialises.
    /// </summary>
    internal static void Track(string command, int processId)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            command = TrimCommand(command);
        }

        lock (gate)
        {
            commands = [new(command, processId), ..commands];
        }
    }

    /// <summary>
    /// Whether the process a snapshot of the list named is still the one it named. The list is
    /// taken once per test process, so a tool closed since then has left a PID that Windows can
    /// hand to anything else - and killing by that PID terminated whatever got it. Asked only on a
    /// hit, which is rare, and answered by reading that process's command line again.
    /// </summary>
    static bool StillRunning(ProcessCommand process) =>
        findAll(CandidateExeNames())
            .Any(_ => _.Process == process.Process &&
                      _.Command == process.Command);

    static void Forget(ProcessCommand process)
    {
        lock (gate)
        {
            commands = commands.Where(_ => !_.Equals(process)).ToList();
        }
    }

    static void TerminateProcessIfExists(in int processId)
    {
        if (tryTerminateProcess(processId))
        {
            Logging.Write($"TerminateProcess. Id: {processId}.");
        }
        else
        {
            Logging.Write($"Process not valid. Id: {processId}.");
        }
    }

    /// <summary>
    /// Find all processes with `% %.%.%` in the command line.
    /// </summary>
    public static IEnumerable<ProcessCommand> FindAll() =>
        findAll(null).OrderByDescending(_ => _.Process);
}