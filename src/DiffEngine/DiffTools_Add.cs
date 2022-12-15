namespace DiffEngine;

public static partial class DiffTools
{
    public static ResolvedTool? AddToolBasedOn(DiffTool basedOn, string name, bool? autoRefresh = null, bool? isMdi = null, bool? supportsText = null, bool? requiresTarget = null, LaunchArguments? launchArguments = null, string? exePath = null, IEnumerable<string>? binaryExtensions = null)
    {
        var existing = resolved.SingleOrDefault(_ => _.Tool == basedOn);
        if (existing == null)
        {
            return null;
        }

        return AddTool(name, autoRefresh ?? existing.AutoRefresh, isMdi ?? existing.IsMdi, supportsText ?? existing.SupportsText, requiresTarget ?? existing.RequiresTarget, launchArguments ?? existing.LaunchArguments, exePath ?? existing.ExePath, binaryExtensions ?? existing.BinaryExtensions);
    }

    public static ResolvedTool? AddTool(string name, bool autoRefresh, bool isMdi, bool supportsText, bool requiresTarget, IEnumerable<string> binaryExtensions, OsSupport osSupport) =>
        AddTool(name, null, autoRefresh, isMdi, supportsText, requiresTarget, binaryExtensions, osSupport);

    public static ResolvedTool? AddTool(string name, bool autoRefresh, bool isMdi, bool supportsText, bool requiresTarget, LaunchArguments launchArguments, string exePath, IEnumerable<string> binaryExtensions) =>
        AddInner(name, null, autoRefresh, isMdi, supportsText, requiresTarget, binaryExtensions, exePath, launchArguments);

    static ResolvedTool? AddTool(string name, DiffTool? diffTool, bool autoRefresh, bool isMdi, bool supportsText, bool requiresTarget, IEnumerable<string> binaryExtensions, OsSupport osSupport)
    {
        if (!OsSettingsResolver.Resolve(osSupport, out var exePath, out var launchArguments))
        {
            return null;
        }

        return AddInner(name, diffTool, autoRefresh, isMdi, supportsText, requiresTarget, binaryExtensions, exePath, launchArguments);
    }

    static ResolvedTool? AddInner(string name, DiffTool? diffTool, bool autoRefresh, bool isMdi, bool supportsText, bool requiresTarget, IEnumerable<string> binaries, string exePath, LaunchArguments launchArguments)
    {
        Guard.AgainstEmpty(name, nameof(name));
        if (resolved.Any(_ => _.Name == name))
        {
            throw new ArgumentException($"Tool with name already exists. Name: {name}", nameof(name));
        }

        if (!WildcardFileFinder.TryFind(exePath, out var resolvedExePath))
        {
            return null;
        }

        var resolvedTool = new ResolvedTool(name, diffTool, resolvedExePath, launchArguments, isMdi, autoRefresh, binaries.ToList(), requiresTarget, supportsText);

        AddResolvedToolAtStart(resolvedTool);

        return resolvedTool;
    }

    static void AddResolvedToolAtStart(ResolvedTool resolvedTool)
    {
        resolved.Insert(0, resolvedTool);
        foreach (var extension in resolvedTool.BinaryExtensions)
        {
            var cleanedExtension = FileExtensions.GetExtension(extension);
            ExtensionLookup[cleanedExtension] = resolvedTool;
        }

        PathLookup[resolvedTool.ExePath] = resolvedTool;
    }

    static void InitTools(bool throwForNoTool, IEnumerable<DiffTool> order)
    {
        var custom = resolved.Where(_ => _.Tool == null).ToList();
        ExtensionLookup.Clear();
        PathLookup.Clear();
        resolved.Clear();

        foreach (var tool in ToolsOrder.Sort(throwForNoTool, order).Reverse())
        {
            AddTool(tool.Tool.ToString(), tool.Tool, tool.AutoRefresh, tool.IsMdi, tool.SupportsText, tool.RequiresTarget, tool.BinaryExtensions, tool.OsSupport);
        }

        custom.Reverse();
        foreach (var tool in custom)
        {
            AddResolvedToolAtStart(tool);
        }
    }
}