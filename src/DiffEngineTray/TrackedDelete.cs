using System.IO;

class TrackedDelete
{
    public TrackedDelete(string file)
    {
        File = file;
        Name = Path.GetFileName(file);
    }

    public string Name { get; }
    public string File { get; }
}