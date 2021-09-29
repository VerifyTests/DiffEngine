namespace DiffEngine;

[DebuggerDisplay("{Command} | Process = {Process}")]
public readonly struct ProcessCommand
{
    /// <summary>
    /// The command line used to launch the process.
    /// </summary>
    public string Command { get; }

    /// <summary>
    /// The process Id.
    /// </summary>
    public int Process { get; }

    public ProcessCommand(string command, in int process)
    {
        Guard.AgainstEmpty(command, nameof(command));
        Command = command;
        Process = process;
    }
}