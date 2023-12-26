namespace DiffEngine;

public static partial class DiffTools
{
    public static bool TryFindByPath(
        string path,
        [NotNullWhen(true)] out ResolvedTool? tool) =>
        PathLookup.TryGetValue(path, out tool);

    public static bool TryFindByExtension(
        string extension,
        [NotNullWhen(true)] out ResolvedTool? tool)
    {
        extension = FileExtensions.GetExtension(extension);
        if (FileExtensions.IsText(extension))
        {
            tool = resolved.FirstOrDefault(_ => _.SupportsText);
            return tool != null;
        }

        return ExtensionLookup.TryGetValue(extension, out tool);
    }

    public static bool TryFindByName(
        DiffTool tool,
        [NotNullWhen(true)] out ResolvedTool? resolvedTool)
    {
        resolvedTool = resolved.SingleOrDefault(_ => _.Tool == tool);
        return resolvedTool != null;
    }

    public static bool TryFindByName(
        string name,
        [NotNullWhen(true)] out ResolvedTool? resolvedTool)
    {
        resolvedTool = resolved.SingleOrDefault(_ => _.Name.Equals(name, StringComparison.Ordinal));
        return resolvedTool != null;
    }
}