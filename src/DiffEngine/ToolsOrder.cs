static class ToolsOrder
{
    public static IEnumerable<Definition> Sort(bool throwForNoTool, IEnumerable<DiffTool> order)
    {
        var allTools = Definitions.Tools.ToList();
        foreach (var diffTool in order)
        {
            var definition = allTools.SingleOrDefault(_ => _.Tool == diffTool);
            if (definition == null)
            {
                if (!throwForNoTool)
                {
                    continue;
                }

                throw new($"`DiffEngine_ToolOrder` is configured to use '{diffTool}' but it is not installed.");
            }

            yield return definition;
            allTools.Remove(definition);
        }

        foreach (var definition in allTools)
        {
            yield return definition;
        }
    }
}