using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using EmptyFiles;

namespace DiffEngine
{
    public static class DiffTools
    {
        static ConcurrentDictionary<string, ResolvedDiffTool> ExtensionLookup = new ConcurrentDictionary<string, ResolvedDiffTool>();
        static ConcurrentBag<ResolvedDiffTool> resolved = new ConcurrentBag<ResolvedDiffTool>();

        public static IEnumerable<ResolvedDiffTool> Resolved { get => resolved; }

        public static string GetPathFor(DiffTool tool)
        {
            if (TryGetPathFor(tool, out var exePath))
            {
                return exePath;
            }
            throw new Exception($"Tool to found: {tool}");
        }

        public static bool TryGetPathFor(DiffTool tool, [NotNullWhen(true)] out string? exePath)
        {
            var resolvedDiffTool = resolved.SingleOrDefault(x => x.Tool == tool);
            if (resolvedDiffTool == null)
            {
                exePath = null;
                return false;
            }

            exePath = resolvedDiffTool.ExePath;
            return true;
        }

        public static bool TryAddCustomTool(
            DiffTool basedOn,
            string name,
            bool? supportsAutoRefresh,
            bool? isMdi,
            bool? supportsText,
            bool? requiresTarget,
            BuildArguments? buildArguments,
            string? exePath,
            IEnumerable<string>? binaryExtensions)
        {
            var existing = resolved.SingleOrDefault(x=>x.Tool ==basedOn);
            if (existing == null)
            {
                return false;
            }

            return TryAddCustomTool(
                name,
                supportsAutoRefresh ?? existing.SupportsAutoRefresh,
                isMdi ?? existing.IsMdi,
                supportsText ?? existing.SupportsText,
                requiresTarget ?? existing.RequiresTarget,
                buildArguments ?? existing.BuildArguments,
                exePath ?? existing.ExePath,
                binaryExtensions ?? existing.BinaryExtensions
            );
        }

        public static void AddTool(
            string name,
            DiffTool toolTool,
            bool autoRefresh,
            bool isMdi,
            bool toolSupportsText,
            bool requiresTarget,
            string[] binaryExtensions,
            BuildArguments? windowsArguments,
            string[] windowsPaths,
            BuildArguments? linuxArguments,
            string[] linuxPaths,
            BuildArguments? osxArguments,
            string[] osxPaths)
        {
            if (!ExeFinder.TryFindExe(windowsPaths, linuxPaths, osxPaths, out var exePath))
            {
                return;
            }

            var buildArguments = ArgumentBuilder.Build(windowsArguments, linuxArguments, osxArguments);
            var diffTool = new ResolvedDiffTool(
                name,
                toolTool,
                exePath,
                buildArguments,
                isMdi,
                autoRefresh,
                binaryExtensions,
                requiresTarget,
                toolSupportsText);

            resolved.Add(diffTool);
            foreach (var ext in binaryExtensions)
            {
                if (!ExtensionLookup.ContainsKey(ext))
                {
                    ExtensionLookup[ext] = diffTool;
                }
            }
        }

        public static bool TryAddCustomTool(
            string name,
            bool autoRefresh,
            bool isMdi,
            bool supportsText,
            bool requiresTarget,
            BuildArguments buildArguments,
            string exePath,
            IEnumerable<string> binaryExtensions)
        {
            Guard.AgainstNullOrEmpty(exePath, nameof(exePath));
            Guard.AgainstNullOrEmpty(name, nameof(name));
            Guard.AgainstNull(binaryExtensions, nameof(binaryExtensions));
            Guard.AgainstNull(buildArguments, nameof(buildArguments));
            if (!File.Exists(exePath))
            {
                return false;
            }

            if (resolved.Any(x => x.Name == name))
            {
                throw new ArgumentException($"Tool with name already exists. Name: {name}", nameof(name));
            }

            var extensions = binaryExtensions.ToArray();
            var tool = new ResolvedDiffTool(
                name,
                exePath,
                buildArguments,
                isMdi,
                autoRefresh,
                extensions,
                requiresTarget,
                supportsText);

            resolved.Add(tool);
            foreach (var extension in extensions)
            {
                var cleanedExtension = Extensions.GetExtension(extension);
                ExtensionLookup[cleanedExtension] = tool;
            }

            return true;
        }

        static DiffTools()
        {
            var result = OrderReader.ReadToolOrder();

            InitTools(result.FoundInEnvVar, result.Order);
        }

        static void InitTools(bool resultFoundInEnvVar, IEnumerable<DiffTool> resultOrder)
        {
            var tools = ToolsByOrder(resultFoundInEnvVar, resultOrder);

            foreach (var tool in tools.Reverse())
            {
                AddTool(tool.Tool.ToString(),
                    tool.Tool,
                    tool.AutoRefresh,
                    tool.IsMdi,
                    tool.SupportsText, tool.RequiresTarget, tool.BinaryExtensions, tool.WindowsArguments, tool.WindowsPaths, tool.LinuxArguments, tool.LinuxPaths, tool.OsxArguments, tool.OsxPaths);
            }
        }

        public static void UseOrder(params DiffTool[] order)
        {
            UseOrder(false, order);
        }

        public static void UseOrder(in bool throwForNoTool, params DiffTool[] order)
        {
            Guard.AgainstNullOrEmpty(order, nameof(order));

            ExtensionLookup.Clear();
#if NETSTANDARD2_1
            resolved.Clear();
#else
            while (!resolved.IsEmpty)
            {
                resolved.TryTake(out _);
            }
#endif
            InitTools(throwForNoTool, order);
        }

        static IEnumerable<Definition> ToolsByOrder(bool throwForNoTool, IEnumerable<DiffTool> order)
        {
            var allTools = Definitions.Tools()
                .ToList();
            foreach (var diffTool in order)
            {
                var definition = allTools.SingleOrDefault(x => x.Tool == diffTool);
                if (definition == null)
                {
                    if (!throwForNoTool)
                    {
                        continue;
                    }

                    throw new Exception($"`DiffEngine.ToolOrder` is configured to use '{diffTool}' but it is not installed.");
                }

                yield return definition;
                allTools.Remove(definition);
            }

            foreach (var definition in allTools)
            {
                yield return definition;
            }
        }

        internal static bool TryFind(
            string extension,
            [NotNullWhen(true)] out ResolvedDiffTool? tool)
        {
            if (Extensions.IsText(extension))
            {
                tool = resolved.FirstOrDefault(x => x.SupportsText);
                return tool != null;
            }

            return ExtensionLookup.TryGetValue(extension, out tool);
        }

        internal static bool TryFind(
            DiffTool tool,
            [NotNullWhen(true)] out ResolvedDiffTool? resolvedTool)
        {
            resolvedTool = resolved.SingleOrDefault(x => x.Tool == tool);
            return resolvedTool != null;
        }

        public static bool IsDetectedFor(DiffTool diffTool, string extensionOrPath)
        {
            var extension = Extensions.GetExtension(extensionOrPath);

            var tool = resolved.SingleOrDefault(_ => _.Tool == diffTool);
            if (tool == null)
            {
                return false;
            }
            if (Extensions.IsText(extension))
            {
                return tool.SupportsText;
            }

            return tool.BinaryExtensions.Contains(extension);
        }
    }
}