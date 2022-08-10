namespace DiffEngine;

[DebuggerDisplay("ExePaths={ExePaths}")]
public class OsSettings
{
    public string ExeName { get; }
    public BuildArguments TargetLeftArguments { get; }
    public BuildArguments TargetRightArguments { get; }
    public string[] ExePaths { get; }

    public OsSettings(
        string exeName,
        BuildArguments targetLeftArguments,
        BuildArguments targetRightArguments,
        params string[] exePaths)
    {
        this.ExeName = exeName;
        TargetLeftArguments = targetLeftArguments;
        TargetRightArguments = targetRightArguments;
        ExePaths = exePaths;
    }
}