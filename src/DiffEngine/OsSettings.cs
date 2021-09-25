namespace DiffEngine
{
    [DebuggerDisplay("ExePaths={ExePaths}")]
    public class OsSettings
    {
        public BuildArguments TargetLeftArguments { get; }
        public BuildArguments TargetRightArguments { get; }
        public string[] ExePaths { get; }

        public OsSettings(
            BuildArguments targetLeftArguments,
            BuildArguments targetRightArguments,
            params string[] exePaths)
        {
            TargetLeftArguments = targetLeftArguments;
            TargetRightArguments = targetRightArguments;
            ExePaths = exePaths;
        }
    }
}