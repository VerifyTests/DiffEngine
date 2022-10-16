namespace DiffEngine;

public static partial class DiffTools
{
    static Dictionary<string, ResolvedTool> ExtensionLookup = new();
    static Dictionary<string, ResolvedTool> PathLookup = new();
    static List<ResolvedTool> resolved = new();

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

    public static bool IsDetectedFor(DiffTool diffTool, string extensionOrPath)
    {
        var extension = FileExtensions.GetExtension(extensionOrPath);

        var tool = resolved.SingleOrDefault(_ => _.Tool == diffTool);
        if (tool == null)
        {
            return false;
        }

        if (FileExtensions.IsText(extension))
        {
            return tool.SupportsText;
        }

        return tool.BinaryExtensions.Contains(extension);
    }
}