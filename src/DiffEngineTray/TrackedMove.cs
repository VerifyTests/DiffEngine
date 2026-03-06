class TrackedMove
{
    public TrackedMove(string temp,
        string target,
        string? exe,
        string? arguments,
        bool canKill,
        Process? process,
        string? group,
        string extension,
        bool killLockingProcess = false)
    {
        Temp = temp;
        Target = target;
        Exe = exe;
        Arguments = arguments;
        Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(target));
        Extension = extension;
        CanKill = canKill;
        Process = process;
        Group = group;
        KillLockingProcess = killLockingProcess;
    }

    public string Extension { get; }
    public string Name { get; }
    public string Temp { get; }
    public string Target { get; }
    public string? Exe { get; }
    public string? Arguments { get; }
    public bool CanKill { get; }
    public Process? Process { get; set; }
    public string? Group { get; }
    public bool KillLockingProcess { get; }
}