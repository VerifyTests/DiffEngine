using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using EmptyFiles;

namespace DiffEngine
{
    public static class DiffTools
    {
        static Dictionary<string, ResolvedTool> ExtensionLookup = new Dictionary<string, ResolvedTool>();
        static List<ResolvedTool> resolved = new List<ResolvedTool>();

        public static IEnumerable<ResolvedTool> Resolved { get => resolved; }

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
                supportsAutoRefresh ?? existing.AutoRefresh,
                isMdi ?? existing.IsMdi,
                supportsText ?? existing.SupportsText,
                requiresTarget ?? existing.RequiresTarget,
                buildArguments ?? existing.BuildArguments,
                exePath ?? existing.ExePath,
                binaryExtensions ?? existing.BinaryExtensions
            );
        }

        static void AddTool(
            string name,
            DiffTool? diffTool,
            bool autoRefresh,
            bool isMdi,
            bool supportsText,
            bool requiresTarget,
            string[] binaryExtensions,
            BuildArguments? windowsArguments,
            IEnumerable<string> windowsPaths,
            BuildArguments? linuxArguments,
            IEnumerable<string> linuxPaths,
            BuildArguments? osxArguments,
            IEnumerable<string> osxPaths)
        {
            if (!ExeFinder.TryFindExe(windowsPaths, linuxPaths, osxPaths, out var exePath))
            {
                return;
            }

            var arguments = ArgumentBuilder.Build(windowsArguments, linuxArguments, osxArguments);
            var resolvedTool = new ResolvedTool(
                name,
                diffTool,
                exePath,
                arguments,
                isMdi,
                autoRefresh,
                binaryExtensions,
                requiresTarget,
                supportsText);

            resolved.Insert(0, resolvedTool);
            foreach (var ext in binaryExtensions)
            {
                ExtensionLookup[ext] = resolvedTool;
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
            var tool = new ResolvedTool(
                name,
                exePath,
                buildArguments,
                isMdi,
                autoRefresh,
                extensions,
                requiresTarget,
                supportsText);

            resolved.Insert(0,tool);
            foreach (var extension in extensions)
            {
                var cleanedExtension = Extensions.GetExtension(extension);
                ExtensionLookup[cleanedExtension] = tool;
            }

            return true;
        }

        static DiffTools()
        {
            Reset();
        }

        internal static void Reset()
        {
            var result = OrderReader.ReadToolOrder();

            InitTools(result.FoundInEnvVar, result.Order);
        }

        static void InitTools(bool resultFoundInEnvVar, IEnumerable<DiffTool> resultOrder)
        {
            ExtensionLookup.Clear();
            resolved.Clear();

            foreach (var tool in ToolsByOrder(resultFoundInEnvVar, resultOrder).Reverse())
            {
                AddTool(
                    tool.Tool.ToString(),
                    tool.Tool,
                    tool.AutoRefresh,
                    tool.IsMdi,
                    tool.SupportsText,
                    tool.RequiresTarget,
                    tool.BinaryExtensions,
                    tool.WindowsArguments,
                    tool.WindowsPaths,
                    tool.LinuxArguments,
                    tool.LinuxPaths,
                    tool.OsxArguments,
                    tool.OsxPaths);
            }
        }

        public static void UseOrder(params DiffTool[] order)
        {
            UseOrder(false, order);
        }

        public static void UseOrder(in bool throwForNoTool, params DiffTool[] order)
        {
            Guard.AgainstNullOrEmpty(order, nameof(order));

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
            [NotNullWhen(true)] out ResolvedTool? tool)
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
            [NotNullWhen(true)] out ResolvedTool? resolvedTool)
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