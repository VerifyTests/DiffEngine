using System.IO;

class TrackedDelete
{
    public TrackedDelete(string file, string? group)
    {
        File = file;
        Group = group;
        Name = Path.GetFileName(file);
    }

    public string Name { get; }
    public string File { get; }
    public string? Group { get; }
}