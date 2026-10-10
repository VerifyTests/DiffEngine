namespace DiffEngine;

public static partial class DiffTools
{
    public static ResolvedTool? AddToolBasedOn(DiffTool basedOn,
        string name,
        bool? autoRefresh = null,
        bool? isMdi = null,
        bool? supportsText = null,
        bool? requiresTarget = null,
        // Null like every other flag here. Defaulting to true made the `?? existing` below dead
        // code for this one alone, so a tool based on one of the five definitions that set it
        // false - the bundled viewer, VS Code, Cursor, MsWordDiff, MsExcelDiff - silently became
        // a ShellExecute launch, and took the inherited CreateNoWindow with it
        bool? useShellExecute = null,
        bool? createNoWindow = null,
        bool? killLockingProcess = null,
        LaunchArguments? launchArguments = null,
        string? exePath = null,
        IEnumerable<string>? binaryExtensions = null)
    {
        if (!ToolLookup.TryGetValue(basedOn, out var existing))
        {
            return null;
        }

        return AddTool(
            name,
            autoRefresh ?? existing.AutoRefresh,
            isMdi ?? existing.IsMdi,
            supportsText ?? existing.SupportsText,
            requiresTarget ?? existing.RequiresTarget,
            useShellExecute ?? existing.UseShellExecute,
            launchArguments ?? existing.LaunchArguments,
            exePath ?? existing.ExePath,
            binaryExtensions ?? existing.BinaryExtensions,
            createNoWindow ?? existing.CreateNoWindow,
            killLockingProcess ?? existing.KillLockingProcess);
    }

    public static ResolvedTool? AddTool(string name, bool autoRefresh, bool isMdi, bool supportsText, bool requiresTarget, bool useShellExecute, IEnumerable<string> binaryExtensions, OsSupport osSupport) =>
        AddTool(name, null, autoRefresh, isMdi, supportsText, requiresTarget, binaryExtensions, osSupport, useShellExecute, createNoWindow: false);

    public static ResolvedTool? AddTool(string name, bool autoRefresh, bool isMdi, bool supportsText, bool requiresTarget, bool useShellExecute, LaunchArguments launchArguments, string exePath, IEnumerable<string> binaryExtensions, bool createNoWindow = false, bool killLockingProcess = false) =>
        AddInner(name, null, autoRefresh, isMdi, supportsText, requiresTarget, binaryExtensions, exePath, launchArguments, useShellExecute, createNoWindow, killLockingProcess);

    static ResolvedTool? AddTool(string name, DiffTool? diffTool, bool autoRefresh, bool isMdi, bool supportsText, bool requiresTarget, IEnumerable<string> binaryExtensions, OsSupport osSupport, bool useShellExecute, bool createNoWindow, bool killLockingProcess = false)
    {
        if (!OsSettingsResolver.Resolve(name, osSupport, out var exePath, out var launchArguments, PreferredCopy(diffTool), WslInterop.Offers(diffTool)))
        {
            return null;
        }

        return AddInner(name, diffTool, autoRefresh, isMdi, supportsText, requiresTarget, binaryExtensions, exePath, launchArguments, useShellExecute, createNoWindow, killLockingProcess);
    }

    /// <summary>
    /// Which of several installed copies of a tool to take, for the one tool where that is this
    /// library's business: the viewer, which it starts with arguments of its own rather than only
    /// the two paths every tool is given. See <see cref="ViewerContract" />.
    /// </summary>
    internal static Func<string, bool>? PreferredCopy(DiffTool? tool)
    {
        if (tool == DiffTool.DiffEngineViewer)
        {
            return ViewerContract.IsMet;
        }

        return null;
    }

    static ResolvedTool? AddInner(string name, DiffTool? diffTool, bool autoRefresh, bool isMdi, bool supportsText, bool requiresTarget, IEnumerable<string> binaries, string exePath, LaunchArguments launchArguments, bool useShellExecute, bool createNoWindow, bool killLockingProcess = false)
    {
        Guard.AgainstEmpty(name, nameof(name));
        if (resolved.Any(_ => _.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Tool with name already exists. Name: {name}", nameof(name));
        }

        if (!WildcardFileFinder.TryFind(exePath, out var resolvedExePath))
        {
            return null;
        }

        var extensions = binaries.ToList();
        // Offered for documents only when the copy resolved can read them, which DiffEngine's
        // bundled copy never can: decided here, where the executable is known, rather than in the
        // definition, which cannot know which copy will be found.
        if (diffTool == DiffTool.DiffEngineViewer &&
            ViewerDocuments.Beside(resolvedExePath))
        {
            extensions.AddRange(DocumentExtensions.Routed.Except(extensions, StringComparer.OrdinalIgnoreCase));
        }

        var tool = new ResolvedTool(
            name,
            diffTool,
            resolvedExePath,
            launchArguments,
            isMdi,
            autoRefresh,
            extensions,
            requiresTarget,
            supportsText,
            useShellExecute,
            createNoWindow,
            killLockingProcess);

        AddResolvedToolAtStart(tool);

        return tool;
    }

    static void AddResolvedToolAtStart(ResolvedTool resolvedTool)
    {
        resolved.Insert(0, resolvedTool);
        foreach (var extension in resolvedTool.BinaryExtensions)
        {
            ExtensionLookup[extension] = resolvedTool;
        }

        PathLookup[resolvedTool.ExePath] = resolvedTool;

        if (resolvedTool.Tool is { } diffTool)
        {
            ToolLookup[diffTool] = resolvedTool;
        }

        // Tools are always prepended, so the most recently inserted text-capable tool is the
        // highest priority one. This mirrors `resolved.FirstOrDefault(_ => _.SupportsText)`.
        if (resolvedTool.SupportsText)
        {
            firstTextTool = resolvedTool;
        }
    }

    static void InitTools(bool throwForNoTool, IEnumerable<DiffTool> order)
    {
        var custom = resolved.Where(_ => _.Tool == null).ToList();
        ExtensionLookup.Clear();
        PathLookup.Clear();
        ToolLookup.Clear();
        firstTextTool = null;
        resolved.Clear();

        foreach (var (definition, requested) in ToolsOrder.Sort(order).Reverse())
        {
            var tool = definition.Tool;
            var added = AddTool(
                tool.ToString(),
                tool,
                definition.AutoRefresh,
                definition.IsMdi,
                definition.SupportsText,
                definition.RequiresTarget,
                definition.BinaryExtensions,
                definition.OsSupport,
                definition.UseShellExecute,
                definition.CreateNoWindow,
                definition.KillLockingProcess);

            // Here rather than in Sort, because this is where being installed is decided: Sort
            // works from Definitions, which holds every tool whether it is on the machine or not,
            // so it could never answer this question
            if (added == null &&
                requested &&
                throwForNoTool)
            {
                throw new($"`DiffEngine_ToolOrder` is configured to use '{tool}' but it is not installed.");
            }
        }

        custom.Reverse();
        foreach (var tool in custom)
        {
            AddResolvedToolAtStart(tool);
        }
    }
}