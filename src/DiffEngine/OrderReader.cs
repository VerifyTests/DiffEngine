using System;
using System.Collections.Generic;
using System.Linq;
using DiffEngine;

static class OrderReader
{
    public readonly struct Result
    {
        public bool FoundInEnvVar { get; }
        public IEnumerable<DiffTool> Order { get; }

        public Result(in bool foundInEnvVar, IEnumerable<DiffTool> order)
        {
            FoundInEnvVar = foundInEnvVar;
            Order = order;
        }
    }

    public static Result ReadToolOrder()
    {
        var diffOrder = Environment.GetEnvironmentVariable("DiffEngine.ToolOrder");
        if (diffOrder == null)
        {
            diffOrder = Environment.GetEnvironmentVariable("Verify.DiffToolOrder");
        }

        var found = !string.IsNullOrWhiteSpace(diffOrder);
        IEnumerable<DiffTool> order;
        if (found)
        {
            order = ParseEnvironment(diffOrder);
        }
        else
        {
            order = Enum.GetValues(typeof(DiffTool)).Cast<DiffTool>();
        }

        return new Result(found, order);
    }

    internal static IEnumerable<DiffTool> ParseEnvironment(string diffOrder)
    {
        foreach (var toolString in diffOrder
            .Split(new[] {',', '|', ' '}, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<DiffTool>(toolString, out var diffTool))
            {
                throw new Exception($"Unable to parse tool from `DiffEngine.ToolOrder` environment variable: {toolString}");
            }

            yield return diffTool;
        }
    }
}