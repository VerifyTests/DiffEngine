using System.Diagnostics;

namespace DiffEngine
{
    [DebuggerDisplay("Arguments={Arguments}, ExePaths={ExePaths}")]
    public class OsSettings
    {
        public BuildArguments Arguments { get; }
        public string[] ExePaths { get; }

        public OsSettings(
            BuildArguments arguments,
            params string[] exePaths)
        {
            Arguments = arguments;
            ExePaths = exePaths;
        }
    }
}