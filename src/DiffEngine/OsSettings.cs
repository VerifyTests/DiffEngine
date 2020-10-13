using System.Diagnostics;

namespace DiffEngine
{
    [DebuggerDisplay("FirstExePath={ExePaths[0]}")]
    public record OsSettings
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