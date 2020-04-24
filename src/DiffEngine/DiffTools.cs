using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using EmptyFiles;

namespace DiffEngine
{
    public static class DiffTools
    {
        static Dictionary<string, ResolvedTool> ExtensionLookup = new Dictionary<string, ResolvedTool>();
        static List<ResolvedTool> resolved = new List<ResolvedTool>();

        public static IEnumerable<ResolvedTool> Resolved { get => resolved; }

        public static ResolvedTool? AddTool(
            string name,
            bool autoRefresh,
            bool isMdi,
            bool supportsText,
            bool requiresTarget,
            BuildArguments arguments,
            string exePath,
            IEnumerable<string> binaryExtensions)
        {
            return AddInner(name, null, autoRefresh, isMdi, supportsText, requiresTarget, binaryExtensions, exePath, arguments);
        }

        public static ResolvedTool? AddToolBasedOn(
            DiffTool basedOn,
            string name,
            bool? autoRefresh = null,
            bool? isMdi = null,
            bool? supportsText = null,
            bool? requiresTarget = null,
            BuildArguments? arguments = null,
            string? exePath = null,
            IEnumerable<string>? binaryExtensions = null)
        {
            var existing = resolved.SingleOrDefault(x => x.Tool == basedOn);
            if (existing == null)
            {
                return null;
            }

            return AddTool(
                name,
                autoRefresh ?? existing.AutoRefresh,
                isMdi ?? existing.IsMdi,
                supportsText ?? existing.SupportsText,
                requiresTarget ?? existing.RequiresTarget,
                arguments ?? existing.Arguments,
                exePath ?? existing.ExePath,
                binaryExtensions ?? existing.BinaryExtensions
            );
        }

        public static ResolvedTool? AddTool(
            string name,
            bool autoRefresh,
            bool isMdi,
            bool supportsText,
            bool requiresTarget,
            IEnumerable<string> binaryExtensions,
            OsSettings? windows = null,
            OsSettings? linux = null,
            OsSettings? osx = null)
        {
            return AddTool(name, null, autoRefresh, isMdi, supportsText, requiresTarget, binaryExtensions, windows, linux, osx);
        }

        static ResolvedTool? AddTool(
            string name,
            DiffTool? diffTool,
            bool autoRefresh,
            bool isMdi,
            bool supportsText,
            bool requiresTarget,
            IEnumerable<string> binaryExtensions,
            OsSettings? windows,
            OsSettings? linux,
            OsSettings? osx)
        {
            if (windows == null &&
                linux == null &&
                osx == null)
            {
                throw new ArgumentException("Must define settings for at least one OS.");
            }
            if (!OsSettingsResolver.Resolve(windows, linux, osx, out var exePath, out var arguments))
            {
                return null;
            }

            return AddInner(name, diffTool, autoRefresh, isMdi, supportsText, requiresTarget, binaryExtensions, exePath, arguments);
        }

        static ResolvedTool? AddInner(
            string name,
            DiffTool? diffTool,
            bool autoRefresh,
            bool isMdi,
            bool supportsText,
            bool requiresTarget,
            IEnumerable<string> binaries,
            string exePath,
            BuildArguments arguments)
        {
            Guard.AgainstNullOrEmpty(name, nameof(name));
            Guard.AgainstNull(binaries, nameof(binaries));
            Guard.AgainstNull(arguments, nameof(arguments));
            if (resolved.Any(x => x.Name == name))
            {
                throw new ArgumentException($"Tool with name already exists. Name: {name}", nameof(name));
            }

            if (!WildcardFileFinder.TryFind(exePath, out var resolvedExePath))
            {
                return null;
            }

            var binariesList = binaries.ToList();
            var resolvedTool = new ResolvedTool(name, diffTool, resolvedExePath, arguments, isMdi, autoRefresh, binariesList, requiresTarget, supportsText);

            resolved.Insert(0, resolvedTool);
            foreach (var extension in binariesList)
            {
                var cleanedExtension = Extensions.GetExtension(extension);
                ExtensionLookup[cleanedExtension] = resolvedTool;
            }

            return resolvedTool;
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

        static void InitTools(bool resultFoundInEnvVar, IEnumerable<DiffTool> tools)
        {
            ExtensionLookup.Clear();
            resolved.Clear();

            foreach (var tool in ToolsOrder.Sort(resultFoundInEnvVar, tools).Reverse())
            {
                AddTool(tool.Tool.ToString(), tool.Tool, tool.AutoRefresh, tool.IsMdi, tool.SupportsText, tool.RequiresTarget, tool.BinaryExtensions, tool.Windows, tool.Linux, tool.Osx);
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