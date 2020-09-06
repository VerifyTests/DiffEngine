using System.Diagnostics;
using System.IO;

class TrackedMove
{
    public TrackedMove(
        string temp,
        string target,
        string exe,
        string arguments,
        bool canKill,
        Process? process)
    {
        Temp = temp;
        Target = target;
        Exe = exe;
        Arguments = arguments;
        Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(target));
        Extension = Path.GetExtension(target).TrimStart('.');
        CanKill = canKill;
        Process = process;
    }

    public string Extension { get; }
    public string Name { get; }
    public string Temp { get; }
    public string Target { get; }
    public string Exe { get; }
    public string Arguments { get; }
    public bool CanKill { get; }
    public Process? Process { get; set; }
}