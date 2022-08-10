using System.Diagnostics.CodeAnalysis;
using EmptyFiles;

namespace DiffEngine;

public static class DiffTools
{
    static Dictionary<string, ResolvedTool> ExtensionLookup = new();
    static Dictionary<string, ResolvedTool> PathLookup = new();
    static List<ResolvedTool> resolved = new();

    public static IEnumerable<ResolvedTool> Resolved => resolved;

    public static ResolvedTool? AddTool(
        string name,
        bool autoRefresh,
        bool isMdi,
        bool supportsText,
        bool requiresTarget,
        BuildArguments targetLeftArguments,
        BuildArguments targetRightArguments,
        string exePath,
        IEnumerable<string> binaryExtensions) =>
        AddInner(
            name,
            null,
            autoRefresh,
            isMdi,
            supportsText,
            requiresTarget,
            binaryExtensions,
            exePath,
            targetLeftArguments, targetRightArguments);

    public static ResolvedTool? AddToolBasedOn(
        DiffTool basedOn,
        string name,
        bool? autoRefresh = null,
        bool? isMdi = null,
        bool? supportsText = null,
        bool? requiresTarget = null,
        BuildArguments? targetLeftArguments = null,
        BuildArguments? targetRightArguments = null,
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
            targetLeftArguments ?? existing.LeftArguments,
            targetRightArguments ?? existing.RightArguments,
            exePath ?? existing.ExePath,
            binaryExtensions ?? existing.BinaryExtensions);
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
        OsSettings? osx = null) =>
        AddTool(
            name,
            null,
            autoRefresh,
            isMdi,
            supportsText,
            requiresTarget,
            binaryExtensions,
            windows,
            linux,
            osx);

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

        if (!OsSettingsResolver.Resolve(windows, linux, osx, out var exePath, out var targetLeftArguments, out var targetRightArguments))
        {
            return null;
        }

        return AddInner(
            name,
            diffTool,
            autoRefresh,
            isMdi,
            supportsText,
            requiresTarget,
            binaryExtensions,
            exePath,
            targetLeftArguments,
            targetRightArguments);
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
        BuildArguments targetLeftArguments,
        BuildArguments targetRightArguments)
    {
        Guard.AgainstEmpty(name, nameof(name));
        if (resolved.Any(x => x.Name == name))
        {
            throw new ArgumentException($"Tool with name already exists. Name: {name}", nameof(name));
        }

        if (!WildcardFileFinder.TryFind(exePath, out var resolvedExePath))
        {
            return null;
        }

        var resolvedTool = new ResolvedTool(
            name,
            diffTool,
            resolvedExePath,
            targetRightArguments,
            targetLeftArguments,
            isMdi,
            autoRefresh,
            binaries.ToList(),
            requiresTarget,
            supportsText);

        AddResolvedToolAtStart(resolvedTool);

        return resolvedTool;
    }

    static void AddResolvedToolAtStart(ResolvedTool resolvedTool)
    {
        resolved.Insert(0, resolvedTool);
        foreach (var extension in resolvedTool.BinaryExtensions)
        {
            var cleanedExtension = Extensions.GetExtension(extension);
            ExtensionLookup[cleanedExtension] = resolvedTool;
        }

        PathLookup[resolvedTool.ExePath] = resolvedTool;
    }

    static DiffTools() =>
        Reset();

    internal static void Reset()
    {
        if (ContinuousTestingDetector.Detected)
        {
            return;
        }

        ExtensionLookup.Clear();
        resolved.Clear();
        var result = OrderReader.ReadToolOrder();

        InitTools(result.FoundInEnvVar, result.Order);
    }

    static void InitTools(bool resultFoundInEnvVar, IEnumerable<DiffTool> tools)
    {
        var custom = resolved.Where(x => x.Tool == null).ToList();
        ExtensionLookup.Clear();
        PathLookup.Clear();
        resolved.Clear();

        foreach (var tool in ToolsOrder.Sort(resultFoundInEnvVar, tools).Reverse())
        {
            AddTool(
                tool.Tool.ToString(),
                tool.Tool,
                tool.AutoRefresh,
                tool.IsMdi,
                tool.SupportsText,
                tool.RequiresTarget,
                tool.BinaryExtensions,
                tool.Windows,
                tool.Linux,
                tool.Osx);
        }

        custom.Reverse();
        foreach (var tool in custom)
        {
            AddResolvedToolAtStart(tool);
        }
    }

    public static void UseOrder(params DiffTool[] order) =>
        UseOrder(false, order);

    public static void UseOrder(in bool throwForNoTool, params DiffTool[] order)
    {
        Guard.AgainstEmpty(order, nameof(order));

        InitTools(throwForNoTool, order);
    }

    public static bool TryFindByPath(
        string path,
        [NotNullWhen(true)] out ResolvedTool? tool) =>
        PathLookup.TryGetValue(path, out tool);

    [Obsolete("Replaced with TryFindByExtension. Made obsolete in version 8.5")]
    public static bool TryFind(
        string extension,
        [NotNullWhen(true)] out ResolvedTool? tool) =>
        TryFindByExtension(extension, out tool);

    public static bool TryFindByExtension(
        string extension,
        [NotNullWhen(true)] out ResolvedTool? tool)
    {
        extension = Extensions.GetExtension(extension);
        if (Extensions.IsText(extension))
        {
            tool = resolved.FirstOrDefault(x => x.SupportsText);
            return tool != null;
        }

        return ExtensionLookup.TryGetValue(extension, out tool);
    }

    [Obsolete("Replaced with TryFindByName. Made obsolete in version 8.5")]
    public static bool TryFind(
        DiffTool tool,
        [NotNullWhen(true)] out ResolvedTool? resolvedTool) =>
        TryFindByName(tool, out resolvedTool);

    public static bool TryFindByName(
        DiffTool tool,
        [NotNullWhen(true)] out ResolvedTool? resolvedTool)
    {
        resolvedTool = resolved.SingleOrDefault(x => x.Tool == tool);
        return resolvedTool != null;
    }

    public static bool TryFindByName(
        string name,
        [NotNullWhen(true)] out ResolvedTool? resolvedTool)
    {
        resolvedTool = resolved.SingleOrDefault(x => x.Name.Equals(name, StringComparison.Ordinal));
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