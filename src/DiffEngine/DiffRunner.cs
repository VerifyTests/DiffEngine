using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EmptyFiles;

namespace DiffEngine
{
    /// <summary>
    /// Manages diff tools processes.
    /// </summary>
    public static class DiffRunner
    {
        static int maxInstancesToLaunch = GetMaxInstances();
        static int launchedInstances;

        public static bool Disabled { get; set; } = IsDisable();

        static int GetMaxInstances()
        {
            var variable = Environment.GetEnvironmentVariable("DiffEngine.MaxInstances");
            if (string.IsNullOrEmpty(variable))
            {
                return 5;
            }

            if (ushort.TryParse(variable, out var result))
            {
                return result;
            }

            throw new Exception($"Could not parse the DiffEngine.MaxInstances environment variable: {variable}");

        }

        static bool IsDisable()
        {
            var variable = Environment.GetEnvironmentVariable("DiffEngine.Disabled");
            return string.Equals(variable, "true", StringComparison.OrdinalIgnoreCase) ||
                   BuildServerDetector.Detected ||
                   ContinuousTestingDetector.Detected;
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

        public static Task<LaunchResult> Launch(DiffTool tool, string tempFile, string targetFile)
        {
            GuardFiles(tempFile, targetFile);

            if (Disabled)
            {
                return Task.FromResult(LaunchResult.Disabled);
            }

            if (!DiffTools.TryFind(tool, out var resolvedTool))
            {
                return Task.FromResult(LaunchResult.NoDiffToolFound);
            }

            return Launch(resolvedTool, tempFile, targetFile);
        }

        /// <summary>
        /// Launch a diff tool for the given paths.
        /// </summary>
        public static Task<LaunchResult> Launch(string tempFile, string targetFile)
        {
            GuardFiles(tempFile, targetFile);
            if (Disabled)
            {
                return Task.FromResult(LaunchResult.Disabled);
            }

            var extension = Extensions.GetExtension(tempFile);

            if (!DiffTools.TryFind(extension, out var diffTool))
            {
                return Task.FromResult(LaunchResult.NoDiffToolFound);
            }

            return Launch(diffTool, tempFile, targetFile);
        }

        public static Task<LaunchResult> Launch(ResolvedTool tool, string tempFile, string targetFile)
        {
            GuardFiles(tempFile, targetFile);
            Guard.AgainstNull(tool, nameof(tool));
            if (Disabled)
            {
                return Task.FromResult(LaunchResult.Disabled);
            }

            var targetExists = File.Exists(targetFile);
            if (tool.RequiresTarget && !targetExists)
            {
                if (!AllFiles.TryCreateFile(targetFile, useEmptyStringForTextFiles: true))
                {
                    return Task.FromResult(LaunchResult.NoEmptyFileForExtension);
                }
            }

            return InnerLaunch(tool, tempFile, targetFile);
        }

        static async Task<LaunchResult> InnerLaunch(ResolvedTool tool, string tempFile, string targetFile)
        {
            var arguments = tool.Arguments(tempFile, targetFile);
            var command = tool.BuildCommand(tempFile, targetFile);
            if (ProcessCleanup.TryGetProcessInfo(command, out var processCommand))
            {
                if (tool.AutoRefresh)
                {
                    await DiffEngineTray.AddMove(tempFile, targetFile, tool.ExePath, arguments, tool.IsMdi!, processCommand.Process);
                    return LaunchResult.AlreadyRunningAndSupportsRefresh;
                }

                if (!tool.IsMdi)
                {
                    ProcessCleanup.Kill(command);
                }
            }

            var instanceCount = Interlocked.Increment(ref launchedInstances);
            if (instanceCount > maxInstancesToLaunch)
            {
                await DiffEngineTray.AddMove(tempFile, targetFile, tool.ExePath, arguments, tool.IsMdi!, null);
                return LaunchResult.TooManyRunningDiffTools;
            }

            try
            {
                var startInfo = new ProcessStartInfo(tool.ExePath, arguments)
                {
                    UseShellExecute = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    var message = $@"Failed to launch diff tool.
{tool.ExePath} {arguments}";
                    throw new Exception(message);
                }

                await DiffEngineTray.AddMove(tempFile, targetFile, tool.ExePath, arguments, !tool.IsMdi, process.Id);
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
    }
}