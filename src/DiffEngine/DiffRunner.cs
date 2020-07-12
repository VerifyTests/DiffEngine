using System;
using System.Diagnostics;
using System.IO;
using EmptyFiles;

namespace DiffEngine
{
    /// <summary>
    /// Manages diff tools processes.
    /// </summary>
    public static class DiffRunner
    {
        static int maxInstancesToLaunch = 5;
        static int launchedInstances;
        public static bool Disabled { get; set; }

        static DiffRunner()
        {
            Disabled = IsDisableByEnv() ||
                       BuildServerDetector.Detected ||
                       ContinuousTestingDetector.Detected;
        }

        static bool IsDisableByEnv()
        {
            var disabledVariable = Environment.GetEnvironmentVariable("DiffEngine.Disabled");
            return string.Equals(disabledVariable, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static void MaxInstancesToLaunch(int value)
        {
            Guard.AgainstNegativeAndZero(value, nameof(value));
            maxInstancesToLaunch = value;
        }

        /// <summary>
        /// Find and kill a diff tool process.
        /// </summary>
        public static void Kill(string tempFile, string targetFile)
        {
            if (Disabled)
            {
                return;
            }

            var extension = Extensions.GetExtension(tempFile);
            if (!DiffTools.TryFind(extension, out var diffTool))
            {
                Logging.Write($"Extension not found. {extension}");
                return;
            }

            var command = diffTool.BuildCommand(tempFile, targetFile);

            if (diffTool.IsMdi)
            {
                Logging.Write($"DiffTool is Mdi so not killing. diffTool: {diffTool.ExePath}");
                return;
            }

            ProcessCleanup.Kill(command);
        }

        public static LaunchResult Launch(DiffTool tool, string tempFile, string targetFile)
        {
            GuardFiles(tempFile, targetFile);

            if (Disabled)
            {
                return LaunchResult.Disabled;
            }

            if (!DiffTools.TryFind(tool, out var resolvedTool))
            {
                return LaunchResult.NoDiffToolFound;
            }

            return Launch(resolvedTool, tempFile, targetFile);
        }

        /// <summary>
        /// Launch a diff tool for the given paths.
        /// </summary>
        public static LaunchResult Launch(string tempFile, string targetFile)
        {
            GuardFiles(tempFile, targetFile);
            if (Disabled)
            {
                return LaunchResult.Disabled;
            }

            var extension = Extensions.GetExtension(tempFile);

            if (!DiffTools.TryFind(extension, out var diffTool))
            {
                return LaunchResult.NoDiffToolFound;
            }

            return Launch(diffTool, tempFile, targetFile);
        }

        public static LaunchResult Launch(ResolvedTool tool, string tempFile, string targetFile)
        {
            GuardFiles(tempFile, targetFile);
            Guard.AgainstNull(tool, nameof(tool));
            if (Disabled)
            {
                return LaunchResult.Disabled;
            }

            if (CheckInstanceCount())
            {
                return LaunchResult.TooManyRunningDiffTools;
            }

            var targetExists = File.Exists(targetFile);
            if (tool.RequiresTarget && !targetExists)
            {
                if (!AllFiles.TryCreateFile(targetFile, useEmptyStringForTextFiles: true))
                {
                    return LaunchResult.NoEmptyFileForExtension;
                }
            }

            return InnerLaunch(tool, tempFile, targetFile);
        }

        static LaunchResult InnerLaunch(ResolvedTool tool, string tempFile, string targetFile)
        {
            launchedInstances++;

            var command = tool.BuildCommand(tempFile, targetFile);
            var isDiffToolRunning = ProcessCleanup.IsRunning(command);
            if (isDiffToolRunning)
            {
                if (tool.AutoRefresh)
                {
                    return LaunchResult.AlreadyRunningAndSupportsRefresh;
                }

                if (!tool.IsMdi)
                {
                    ProcessCleanup.Kill(command);
                }
            }

            var arguments = tool.Arguments(tempFile, targetFile);
            try
            {
                var startInfo = new ProcessStartInfo(tool.ExePath, arguments)
                {
                    UseShellExecute = tool.ShellExecute
                };
                Process.Start(startInfo);
                return LaunchResult.StartedNewInstance;
            }
            catch (Exception exception)
            {
                var message = $@"Failed to launch diff tool.
{tool.ExePath} {arguments}";
                throw new Exception(message, exception);
            }
        }

        static void GuardFiles(string tempFile, string targetFile)
        {
            Guard.FileExists(tempFile, nameof(tempFile));
            Guard.AgainstNullOrEmpty(targetFile, nameof(targetFile));
        }

        static bool CheckInstanceCount()
        {
            return launchedInstances >= maxInstancesToLaunch;
        }
    }
}