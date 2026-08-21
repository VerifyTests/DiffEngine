namespace DiffEngine;

public static class ProcessCleanup
{
    static List<ProcessCommand> commands;
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

    /// <summary>
    /// The processes as of the last <see cref="Refresh"/>. A snapshot, so callers that need to
    /// know what is running now go through <see cref="IsRunning"/> or <see cref="Kill"/>, both of
    /// which take their own.
    /// </summary>
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

        // The list was filled once by the static constructor and nothing in the library refreshed
        // it, so this matched against whatever was running the first time anything touched
        // DiffEngine. In one process a Launch followed by a Kill for the same pair logged "No
        // matching commands" and left the tool open. It also keeps the PID as fresh as this can
        // make it: a process that has since exited may have had its id reused, and terminating
        // from a stale snapshot kills whatever holds it now
        Refresh();

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

        // As for Kill: the question is what is running now. Against the startup snapshot a tool
        // launched later was never seen again, so an AutoRefresh tool opened a second window
        // instead of being reused and every relaunch spent another MaxInstance slot
        Refresh();

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
        findAll(null).OrderByDescending(_ => _.Process);
}