namespace DiffEngine
{
    public class OsSettings
    {
        public BuildArguments Arguments { get; }
        public string[] ExePaths { get; }

        public OsSettings(
            BuildArguments arguments,
            string[] exePaths)
        {
            Guard.AgainstNull(arguments, nameof(arguments));
            Guard.AgainstNull(arguments, nameof(exePaths));
            Arguments = arguments;
            ExePaths = exePaths;
        }
    }
}