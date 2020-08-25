using System.IO;

class TrackedMove
{
    public TrackedMove(
        string temp,
        string target,
        bool canKill,
        int? processId)
    {
        Temp = temp;
        Target = target;
        Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(target));
        Extension = Path.GetExtension(target).TrimStart('.');
        CanKill = canKill;
        ProcessId = processId;
    }

    public string Extension { get; }
    public string Name { get; }
    public string Temp { get; }
    public string Target { get; }
    public bool CanKill { get; }
    public int? ProcessId { get; set; }
}