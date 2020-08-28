using System;
using System.IO;

class TrackedMove
{
    public TrackedMove(
        string temp,
        string target,
        string exe,
        string arguments,
        bool canKill,
        int? processId,
        DateTime? processStartTime)
    {
        Temp = temp;
        Target = target;
        Exe = exe;
        Arguments = arguments;
        Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(target));
        Extension = Path.GetExtension(target).TrimStart('.');
        CanKill = canKill;
        ProcessId = processId;
        ProcessStartTime = processStartTime;
    }

    public string Extension { get; }
    public string Name { get; }
    public string Temp { get; }
    public string Target { get; }
    public string Exe { get; }
    public string Arguments { get; }
    public bool CanKill { get; }
    public int? ProcessId { get; set; }
    public DateTime? ProcessStartTime { get; }
}