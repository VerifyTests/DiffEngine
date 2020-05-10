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
            Guard.AgainstNull(arguments, nameof(arguments));
            Guard.AgainstNull(exePaths, nameof(exePaths));
            Arguments = arguments;
            ExePaths = exePaths;
        }
    }
}