using System;
using System.Collections.Generic;
using System.IO;

class TrackedMove
{
    public TrackedMove(
        string temp,
        string target,
        string exe,
        string arguments,
        bool canKill)
    {
        Temp = temp;
        Target = target;
        Exe = exe;
        Arguments = arguments;
        Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(target));
        Extension = Path.GetExtension(target).TrimStart('.');
        CanKill = canKill;
    }

    public string Extension { get; }
    public string Name { get; }
    public string Temp { get; }
    public string Target { get; }
    public string Exe { get; }
    public string Arguments { get; }
    public bool CanKill { get; }

    public void AddProcess((int id, DateTime startTime) process)
    {
        Processes.Add(process);
    }
    public void AddProcess(int id, DateTime startTime)
    {
        Processes.Add((id, startTime));
    }
    public List<( int id, DateTime startTime)> Processes = new List<(int id, DateTime startTime)>();
}