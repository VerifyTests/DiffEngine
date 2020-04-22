using System.Collections.Generic;

namespace DiffEngine
{
    public class ResolvedDiffTool
    {
        public string Name { get; }
        public DiffTool? Tool { get; }
        public string ExePath { get; }
        public BuildArguments BuildArguments { get; }
        public bool IsMdi { get; }
        public bool SupportsAutoRefresh { get; }
        public IReadOnlyList<string> BinaryExtensions { get; }
        public bool RequiresTarget { get; }
        public bool SupportsText { get; }

        public string BuildCommand(string tempFile, string targetFile)
        {
            return $"\"{ExePath}\" {BuildArguments(tempFile, targetFile)}";
        }

        internal ResolvedDiffTool(
            string name,
            DiffTool tool,
            string exePath,
            BuildArguments buildArguments,
            bool isMdi,
            bool supportsAutoRefresh,
            string[] binaryExtensions,
            bool requiresTarget,
            bool supportsText)
        {
            Name = name;
            Tool = tool;
            ExePath = exePath;
            BuildArguments = buildArguments;
            IsMdi = isMdi;
            SupportsAutoRefresh = supportsAutoRefresh;
            BinaryExtensions = binaryExtensions;
            RequiresTarget = requiresTarget;
            SupportsText = supportsText;
        }

        public ResolvedDiffTool(
            string name,
            string exePath,
            BuildArguments buildArguments,
            bool isMdi,
            bool supportsAutoRefresh,
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
            SupportsAutoRefresh = supportsAutoRefresh;
            BinaryExtensions = binaryExtensions;
            RequiresTarget = requiresTarget;
            SupportsText = supportsText;
        }
    }
}