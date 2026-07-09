namespace DiffEngine;

public static partial class DiffTools
{
    static Dictionary<string, ResolvedTool> ExtensionLookup = [];
    static Dictionary<string, ResolvedTool> PathLookup = [];
    static Dictionary<DiffTool, ResolvedTool> ToolLookup = [];
    static ResolvedTool? firstTextTool;
    static List<ResolvedTool> resolved = [];

    public static IEnumerable<ResolvedTool> Resolved => resolved;

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

        InitTools(result.UsedToolOrderEnvVar, result.Order);
    }

    public static void UseOrder(params DiffTool[] order) =>
        UseOrder(false, order);

    public static void UseOrder(in bool throwForNoTool, params DiffTool[] order)
    {
        Guard.AgainstEmpty(order, nameof(order));

        InitTools(throwForNoTool, order);
    }

    public static bool IsDetectedForFile(DiffTool diffTool, string path)
    {
        if (!ToolLookup.TryGetValue(diffTool, out var tool))
        {
            return false;
        }

        if (FileExtensions.IsTextFile(path))
        {
            return tool.SupportsText;
        }

        var extension = Path.GetExtension(path);
        return tool.BinaryExtensions.Contains(extension);
    }

    public static bool IsDetectedForExtension(DiffTool diffTool, string extension)
    {
        if (!ToolLookup.TryGetValue(diffTool, out var tool))
        {
            return false;
        }

        if (FileExtensions.IsTextExtension(extension))
        {
            return tool.SupportsText;
        }

        return tool.BinaryExtensions.Contains(extension);
    }
}