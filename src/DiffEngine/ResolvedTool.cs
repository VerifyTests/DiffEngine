using System.Collections.Generic;

namespace DiffEngine
{
    public class ResolvedTool
    {
        public string Name { get; }
        public DiffTool? Tool { get; }
        public string ExePath { get; }
        public BuildArguments BuildArguments { get; }
        public bool IsMdi { get; }
        public bool AutoRefresh { get; }
        public IReadOnlyList<string> BinaryExtensions { get; }
        public bool RequiresTarget { get; }
        public bool SupportsText { get; }

        public string BuildCommand(string tempFile, string targetFile)
        {
            return $"\"{ExePath}\" {BuildArguments(tempFile, targetFile)}";
        }

        internal ResolvedTool(
            string name,
            DiffTool tool,
            string exePath,
            BuildArguments buildArguments,
            bool isMdi,
            bool autoRefresh,
            string[] binaryExtensions,
            bool requiresTarget,
            bool supportsText)
        {
            Name = name;
            Tool = tool;
            ExePath = exePath;
            BuildArguments = buildArguments;
            IsMdi = isMdi;
            AutoRefresh = autoRefresh;
            BinaryExtensions = binaryExtensions;
            RequiresTarget = requiresTarget;
            SupportsText = supportsText;
        }

        public ResolvedTool(
            string name,
            string exePath,
            BuildArguments buildArguments,
            bool isMdi,
            bool autoRefresh,
            string[] binaryExtensions,
            bool requiresTarget,
            bool supportsText)
        {
            Guard.FileExists(exePath, nameof(exePath));
            Guard.AgainstNullOrEmpty(name, nameof(name));
            Guard.AgainstNull(binaryExtensions, nameof(binaryExtensions));
            Guard.AgainstNull(buildArguments, nameof(buildArguments));
            Name = name;
            ExePath = exePath;
            BuildArguments = buildArguments;
            IsMdi = isMdi;
            AutoRefresh = autoRefresh;
            BinaryExtensions = binaryExtensions;
            RequiresTarget = requiresTarget;
            SupportsText = supportsText;
        }
    }
}