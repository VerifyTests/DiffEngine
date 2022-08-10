namespace DiffEngine;

[DebuggerDisplay("{ExeName} |ExePaths={ExePaths}")]
public class OsSettings
{
    public string ExeName { get; }
    public BuildArguments LeftArguments { get; }
    public BuildArguments RightArguments { get; }
    public string[] SearchDirectories { get; }

    public OsSettings(
        string exeName,
        BuildArguments leftArguments,
        BuildArguments rightArguments,
        params string[] searchDirectories)
    {
        ExeName = exeName;
        LeftArguments = leftArguments;
        RightArguments = rightArguments;
        SearchDirectories = searchDirectories;
    }
}