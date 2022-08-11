static class OrderReader
{
    public readonly struct Result
    {
        public bool UsedToolOrderEnvVar { get; }
        public IEnumerable<DiffTool> Order { get; }

        public Result(in bool usedToolOrderEnvVar, IEnumerable<DiffTool> order)
        {
            UsedToolOrderEnvVar = usedToolOrderEnvVar;
            Order = order;
        }
    }

    public static Result ReadToolOrder()
    {
        var diffOrder = Environment.GetEnvironmentVariable("DiffEngine_ToolOrder");

        IEnumerable<DiffTool> order;
        if (string.IsNullOrWhiteSpace(diffOrder))
        {
            order = Enum.GetValues(typeof(DiffTool)).Cast<DiffTool>();
            return new(false, order);
        }

        order = ParseEnvironment(diffOrder);
        return new(true, order);
    }

    internal static IEnumerable<DiffTool> ParseEnvironment(string diffOrder)
    {
        foreach (var toolString in diffOrder
                     .Split(new[] {',', '|', ' '}, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<DiffTool>(toolString, out var diffTool))
            {
                throw new($"Unable to parse tool from `DiffEngine_ToolOrder` environment variable: {toolString}");
            }

            yield return diffTool;
        }
    }
}