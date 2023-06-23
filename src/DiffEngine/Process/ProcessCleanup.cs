namespace DiffEngine;

public static class ProcessCleanup
{
    static List<ProcessCommand> commands;
    static Func<IEnumerable<ProcessCommand>> findAll;
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
        commands = FindAll().ToList();

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
            .Where(_ => _.Command == command).ToList();
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
        return !process.Equals(default(ProcessCommand));
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
        findAll().OrderByDescending(_ => _.Process);
}