using System.Diagnostics;

namespace DiffEngine
{
    [DebuggerDisplay("ExePaths={ExePaths}")]
    public class OsSettings
    {
        public BuildArguments TargetRightArguments { get; }
        public string[] ExePaths { get; }

        public OsSettings(
            BuildArguments targetRightArguments,
            params string[] exePaths)
        {
            TargetRightArguments = targetRightArguments;
            ExePaths = exePaths;
        }
    }
}