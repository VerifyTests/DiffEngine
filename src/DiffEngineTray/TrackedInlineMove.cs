class TrackedInlineMove
{
    public TrackedInlineMove(
        string temp,
        string target,
        string patchFile,
        string? stagedVerified,
        string? group,
        string? exe,
        string? arguments)
    {
        Temp = temp;
        Target = target;
        PatchFile = patchFile;
        StagedVerified = stagedVerified;
        Group = group;
        Exe = exe;
        Arguments = arguments;
        Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(temp));
    }

    public string Temp { get; }
    public string Target { get; }
    public string PatchFile { get; }
    public string? StagedVerified { get; }
    public string? Group { get; }
    public string? Exe { get; }
    public string? Arguments { get; }
    public string Name { get; }
    public Process? Process { get; set; }
}
