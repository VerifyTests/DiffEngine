using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using EmptyFiles;

namespace DiffEngine
{
    /// <summary>
    /// Manages diff tools processes.
    /// </summary>
    public static partial class DiffRunner
    {
        public static bool Disabled { get; set; } = DisabledChecker.IsDisable();

        public static void MaxInstancesToLaunch(int value)
        {
            Guard.AgainstNegativeAndZero(value, nameof(value));
            MaxInstance.Set(value);
        }

        public static LaunchResult Launch(DiffTool tool, string tempFile, string targetFile)
        {
            GuardFiles(tempFile, targetFile);

            return InnerLaunch(
                (out ResolvedTool? resolved) => DiffTools.TryFind(tool, out resolved),
                tempFile,
                targetFile);
        }

        public static Task<LaunchResult> LaunchAsync(DiffTool tool, string tempFile, string targetFile)
        {
            GuardFiles(tempFile, targetFile);

            return InnerLaunchAsync(
                (out ResolvedTool? resolved) => DiffTools.TryFind(tool, out resolved),
                tempFile,
                targetFile);
        }

        /// <summary>
        /// Launch a diff tool for the given paths.
        /// </summary>
        public static LaunchResult Launch(string tempFile, string targetFile)
        {
            GuardFiles(tempFile, targetFile);

            return InnerLaunch(
                (out ResolvedTool? tool) =>
                {
                    var extension = Extensions.GetExtension(tempFile);
                    return DiffTools.TryFind(extension, out tool);
                },
                tempFile,
                targetFile);
        }

        /// <summary>
        /// Launch a diff tool for the given paths.
        /// </summary>
        public static Task<LaunchResult> LaunchAsync(string tempFile, string targetFile)
        {
            GuardFiles(tempFile, targetFile);

            return InnerLaunchAsync(
                (out ResolvedTool? tool) =>
                {
                    var extension = Extensions.GetExtension(tempFile);
                    return DiffTools.TryFind(extension, out tool);
                },
                tempFile,
                targetFile);
        }

        public static LaunchResult Launch(ResolvedTool tool, string tempFile, string targetFile)
        {
            GuardFiles(tempFile, targetFile);

            return InnerLaunch(
                (out ResolvedTool? resolvedTool) =>
                {
                    resolvedTool = tool;
                    return true;
                },
                tempFile,
                targetFile);
        }

        public static Task<LaunchResult> LaunchAsync(ResolvedTool tool, string tempFile, string targetFile)
        {
            GuardFiles(tempFile, targetFile);

            return InnerLaunchAsync(
                (out ResolvedTool? resolvedTool) =>
                {
                    resolvedTool = tool;
                    return true;
                },
                tempFile,
                targetFile);
        }

        static LaunchResult InnerLaunch(TryResolveTool tryResolveTool, string tempFile, string targetFile)
        {
            if (ShouldExitLaunch(tryResolveTool, targetFile, out var tool, out var result))
            {
                DiffEngineTray.AddMove(tempFile, targetFile, null, null, false, null);
                return result.Value;
            }

            tool.CommandAndArguments(tempFile, targetFile, out var arguments, out var command);

            if (ProcessCleanup.TryGetProcessInfo(command, out var processCommand))
            {
                if (tool.AutoRefresh)
                {
                    DiffEngineTray.AddMove(tempFile, targetFile, tool.ExePath, arguments, tool.IsMdi!, processCommand.Process);
                    return LaunchResult.AlreadyRunningAndSupportsRefresh;
                }

                KillIfMdi(tool, command);
            }

            if (MaxInstance.Reached())
            {
                DiffEngineTray.AddMove(tempFile, targetFile, tool.ExePath, arguments, tool.IsMdi!, null);
                return LaunchResult.TooManyRunningDiffTools;
            }

            var processId = LaunchProcess(tool, arguments);

            DiffEngineTray.AddMove(tempFile, targetFile, tool.ExePath, arguments, !tool.IsMdi, processId);

            return LaunchResult.StartedNewInstance;
        }

        static async Task<LaunchResult> InnerLaunchAsync(TryResolveTool tryResolveTool, string tempFile, string targetFile)
        {
            if (ShouldExitLaunch(tryResolveTool, targetFile, out var tool, out var result))
            {
                await DiffEngineTray.AddMoveAsync(tempFile, targetFile, null, null, false, null);
                return result.Value;
            }

            tool.CommandAndArguments(tempFile, targetFile, out var arguments, out var command);

            var canKill = !tool.IsMdi;
            if (ProcessCleanup.TryGetProcessInfo(command, out var processCommand))
            {
                if (tool.AutoRefresh)
                {
                    await DiffEngineTray.AddMoveAsync(tempFile, targetFile, tool.ExePath, arguments, canKill, processCommand.Process);
                    return LaunchResult.AlreadyRunningAndSupportsRefresh;
                }

                KillIfMdi(tool, command);
            }

            if (MaxInstance.Reached())
            {
                await DiffEngineTray.AddMoveAsync(tempFile, targetFile, tool.ExePath, arguments, canKill, null);
                return LaunchResult.TooManyRunningDiffTools;
            }

            var processId = LaunchProcess(tool, arguments);

            await DiffEngineTray.AddMoveAsync(tempFile, targetFile, tool.ExePath, arguments, canKill, processId);

            return LaunchResult.StartedNewInstance;
        }

        static bool ShouldExitLaunch(
            TryResolveTool tryResolveTool,
            string targetFile,
            [NotNullWhen(false)] out ResolvedTool? tool,
            [NotNullWhen(true)] out LaunchResult? result)
        {
            if (Disabled)
            {
                result = LaunchResult.Disabled;
                tool = null;
                return true;
            }

            if (!tryResolveTool(out tool))
            {
                result = LaunchResult.NoDiffToolFound;
                return true;
            }

            if (!TryCreate(tool, targetFile))
            {
                result = LaunchResult.NoEmptyFileForExtension;
                return true;
            }

            result = null;
            return false;
        }

        static bool TryCreate(ResolvedTool tool, string targetFile)
        {
            var targetExists = File.Exists(targetFile);
            if (tool.RequiresTarget && !targetExists)
            {
                if (!AllFiles.TryCreateFile(targetFile, useEmptyStringForTextFiles: true))
                {
                    return false;
                }
            }

            return true;
        }

        static int LaunchProcess(ResolvedTool tool, string arguments)
        {
            var startInfo = new ProcessStartInfo(tool.ExePath, arguments)
            {
                UseShellExecute = true
            };
            try
            {
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    return process.Id;
                }

                var message = $@"Failed to launch diff tool.
{tool.ExePath} {arguments}";
                throw new(message);
            }
            catch (Exception exception)
            {
                var message = $@"Failed to launch diff tool.
{tool.ExePath} {arguments}";
                throw new(message, exception);
            }
        }

        static void KillIfMdi(ResolvedTool tool, string command)
        {
            if (!tool.IsMdi)
            {
                ProcessCleanup.Kill(command);
            }
        }

        static void GuardFiles(string tempFile, string targetFile)
        {
            Guard.FileExists(tempFile, nameof(tempFile));
            Guard.AgainstNullOrEmpty(targetFile, nameof(targetFile));
        }

        delegate bool TryResolveTool([NotNullWhen(true)] out ResolvedTool? resolved);
    }
}