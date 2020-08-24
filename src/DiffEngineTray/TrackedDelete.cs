class TrackedDelete
{
    public TrackedDelete(string file)
    {
        File = file;
    }

    public string Name { get; set; }= null!;
    public string File { get; }
}