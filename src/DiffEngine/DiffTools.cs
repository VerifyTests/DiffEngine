﻿using System;
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
            var existing = resolved.SingleOrDefault(x => x.Tool == basedOn);
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

        public static void AddTool(
            string name,
            bool autoRefresh,
            bool isMdi,
            bool supportsText,
            bool requiresTarget,
            string[] binaryExtensions,
            OsSettings? windows,
            OsSettings? linux,
            OsSettings? osx)
        {
            AddTool(name, null, autoRefresh, isMdi, supportsText, requiresTarget, binaryExtensions, windows, linux, osx);
        }

        static void AddTool(
            string name,
            DiffTool? diffTool,
            bool autoRefresh,
            bool isMdi,
            bool supportsText,
            bool requiresTarget,
            string[] binaryExtensions,
            OsSettings? windows,
            OsSettings? linux,
            OsSettings? osx)
        {
            if (!ExeFinder.TryFindExe(windows, linux, osx, out var exePath, out var arguments))
            {
                return;
            }
            AddInner(name, diffTool, autoRefresh, isMdi, supportsText, requiresTarget, binaryExtensions, exePath, arguments);
        }

        static void AddInner(
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
            var binariesList = binaries.ToList();
            var resolvedTool = new ResolvedTool(name, diffTool, exePath, arguments, isMdi, autoRefresh, binariesList, requiresTarget, supportsText);

            resolved.Insert(0, resolvedTool);
            foreach (var extension in binariesList)
            {
                var cleanedExtension = Extensions.GetExtension(extension);
                ExtensionLookup[cleanedExtension] = resolvedTool;
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

            AddInner(name, null, autoRefresh, isMdi, supportsText, requiresTarget, binaryExtensions, exePath, buildArguments);

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

            foreach (var tool in ToolsOrder.ToolsByOrder(resultFoundInEnvVar, resultOrder).Reverse())
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