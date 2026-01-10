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
        if (FileExtensions.IsTextExtension(extension))
        {
            tool = resolved.FirstOrDefault(_ => _.SupportsText);
            return tool != null;
        }

        return ExtensionLookup.TryGetValue(extension, out tool);
    }

    public static bool TryFindForText([NotNullWhen(true)] out ResolvedTool? tool)
    {
        tool = resolved.FirstOrDefault(_ => _.SupportsText);
        return tool != null;
    }

    public static bool TryFindForInputFilePath(
        string path,
        [NotNullWhen(true)] out ResolvedTool? tool)
    {
        if (FileExtensions.IsTextFile(path))
        {
            tool = resolved.FirstOrDefault(_ => _.SupportsText);
            return tool != null;
        }

        return ExtensionLookup.TryGetValue(Path.GetExtension(path), out tool);
    }

    public static bool TryFindForInputFilePath(
        CharSpan path,
        [NotNullWhen(true)] out ResolvedTool? tool)
    {
        if (FileExtensions.IsTextFile(path))
        {
            tool = resolved.FirstOrDefault(_ => _.SupportsText);
            return tool != null;
        }

        var extension = Path.GetExtension(path).ToString();
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
        resolvedTool = resolved.SingleOrDefault(_ => _.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return resolvedTool != null;
    }
}