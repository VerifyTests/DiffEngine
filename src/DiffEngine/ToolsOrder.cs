static class ToolsOrder
{
    /// <summary>
    /// The requested tools first, in the order asked for, then everything else. Each is flagged
    /// with whether it was asked for, so the caller can tell a tool the user named and could not
    /// have from one that simply is not installed on this machine and was never mentioned.
    /// </summary>
    public static IEnumerable<(Definition Definition, bool Requested)> Sort(IEnumerable<DiffTool> order)
    {
        var allTools = Definitions.Tools.ToList();
        // Distinct, because a repeated name is a typo rather than a request for two of something.
        // Without it the second occurrence found nothing - the first had already removed it - and
        // that was reported as "is not installed", which was both untrue and, from a static
        // constructor, permanent: DiffEngine_ToolOrder=VisualStudio,VisualStudio turned every
        // later use of DiffTools into a TypeInitializationException
        foreach (var diffTool in order.Distinct())
        {
            var definition = allTools.SingleOrDefault(_ => _.Tool == diffTool);
            if (definition == null)
            {
                continue;
            }

            yield return (definition, true);
            allTools.Remove(definition);
        }

        foreach (var definition in allTools)
        {
            yield return (definition, false);
        }
    }
}
