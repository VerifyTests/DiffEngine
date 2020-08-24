class TrackedMove
{
    public TrackedMove(
        string temp,
        string target,
        bool isMdi,
        bool autoRefresh,
        int processId)
    {
        Temp = temp;
        Target = target;
        IsMdi = isMdi;
        AutoRefresh = autoRefresh;
        ProcessId = processId;
    }

    public string Temp { get; }
    public string Target { get; }
    public bool IsMdi { get; }
    public bool AutoRefresh { get; }
    public int ProcessId { get; }
}