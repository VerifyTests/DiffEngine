using System.Collections.Generic;
using System.Diagnostics;

namespace DiffEngine
{
    [DebuggerDisplay("{Name} {ExePath}, Refresh={AutoRefresh}, Mdi={IsMdi}, RequiresTarget={RequiresTarget}, SupportsText={SupportsText}")]
    public class ResolvedTool
    {
        public string Name { get; }
        public DiffTool? Tool { get; }
        public string ExePath { get; }
        public BuildArguments TargetRightArguments { get; }
        public BuildArguments TargetLeftArguments { get; }
        public bool IsMdi { get; }
        public bool AutoRefresh { get; }
        public IReadOnlyList<string> BinaryExtensions { get; }
        public bool RequiresTarget { get; }
        public bool SupportsText { get; }

        internal void CommandAndArguments(string tempFile, string targetFile, out string arguments, out string command)
        {
            arguments = GetArguments(tempFile, targetFile);
            command = $"\"{ExePath}\" {arguments}";
        }

        public string BuildCommand(string tempFile, string targetFile)
        {
            return $"\"{ExePath}\" {GetArguments(tempFile, targetFile)}";
        }

        string GetArguments(string tempFile, string targetFile)
        {
            if (TargetPositionHelper.TargetOnLeft)
            {
                return TargetLeftArguments(tempFile, targetFile);
            }

            return TargetRightArguments(tempFile, targetFile);
        }


        internal ResolvedTool(string name,
            DiffTool? tool,
            string exePath,
            BuildArguments targetRightArguments,
            BuildArguments targetLeftArguments,
            bool isMdi,
            bool autoRefresh,
            IReadOnlyList<string> binaryExtensions,
            bool requiresTarget,
            bool supportsText)
        {
            Name = name;
            Tool = tool;
            ExePath = exePath;
            TargetRightArguments = targetRightArguments;
            TargetLeftArguments = targetLeftArguments;
            IsMdi = isMdi;
            AutoRefresh = autoRefresh;
            BinaryExtensions = binaryExtensions;
            RequiresTarget = requiresTarget;
            SupportsText = supportsText;
        }

        public ResolvedTool(
            string name,
            string exePath,
            BuildArguments targetRightArguments,
            BuildArguments targetLeftArguments,
            bool isMdi,
            bool autoRefresh,
            string[] binaryExtensions,
            bool requiresTarget,
            bool supportsText)
        {
            Guard.FileExists(exePath, nameof(exePath));
            Guard.AgainstEmpty(name, nameof(name));
            Name = name;
            ExePath = exePath;
            TargetRightArguments = targetRightArguments;
            TargetLeftArguments = targetLeftArguments;
            IsMdi = isMdi;
            AutoRefresh = autoRefresh;
            BinaryExtensions = binaryExtensions;
            RequiresTarget = requiresTarget;
            SupportsText = supportsText;
        }
    }
}