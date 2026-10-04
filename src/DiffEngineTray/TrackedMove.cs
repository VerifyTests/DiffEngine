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
        bool killLockingProcess = false,
        bool isViewer = false,
        string? source = null)
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
        IsViewer = isViewer;
        Source = source;
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

    /// <summary>
    /// The received file of the pending move this one was derived from, or null: a page of a
    /// document whose document is pending too. Only carried here. The tray lists the two as the
    /// two pending files they are, and a viewer showing the queue is what puts one beneath the
    /// other, since it is the one drawing the document.
    /// </summary>
    public string? Source { get; }

    /// <summary>
    /// Whether the tool showing this pair is the viewer, which is the one tool that opens no
    /// process of its own for it.
    /// </summary>
    public bool IsViewer { get; }

    /// <summary>
    /// Whether something is showing this pair right now, which is what "accept all open" acts on.
    /// <para>
    /// For every other tool that is a live process, because DiffRunner started one window per
    /// pair and recorded it. A viewer backed pair has none by construction - it is drawn as a row
    /// in the one window holding every pending pair, which is why nothing may kill it and why no
    /// process id is sent - so the process test alone left those rows out of every "accept all
    /// open" while the window they were drawn in sat on the screen.
    /// </para>
    /// </summary>
    public bool IsOpen =>
        IsViewer ||
        Process is {HasExited: false};
}