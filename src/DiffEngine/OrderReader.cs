static class OrderReader
{
    public record Result(bool UsedToolOrderEnvVar, IEnumerable<DiffTool> Order);

    static Result defaultResult = new(false, Enum.GetValues<DiffTool>());

    public static Result ReadToolOrder()
    {
        var diffOrder = Environment.GetEnvironmentVariable("DiffEngine_ToolOrder");

        if (string.IsNullOrWhiteSpace(diffOrder))
        {
            return defaultResult;
        }

        var order = ParseEnvironment(diffOrder);
        return new(true, order);
    }

    static char[] environmentSeparators = [',', '|', ' '];

    internal static IEnumerable<DiffTool> ParseEnvironment(string diffOrder)
    {
        foreach (var toolString in diffOrder
                     .Split(environmentSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<DiffTool>(toolString, out var diffTool))
            {
                throw new($"Unable to parse tool from `DiffEngine_ToolOrder` environment variable: {toolString}");
            }

            yield return diffTool;
        }
    }
}