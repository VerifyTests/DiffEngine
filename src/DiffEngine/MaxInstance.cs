using System;
using System.Threading;

static class MaxInstance
{
    static int maxInstancesToLaunch = GetMaxInstances();
    static int launchedInstances;

    static int GetMaxInstances()
    {
        var variable = Environment.GetEnvironmentVariable("DiffEngine_MaxInstances");
        if (string.IsNullOrEmpty(variable))
        {
            return 5;
        }

        if (ushort.TryParse(variable, out var result))
        {
            return result;
        }

        throw new($"Could not parse the DiffEngine_MaxInstances environment variable: {variable}");
    }

    public static void Set(int value)
    {
        Guard.AgainstNegativeAndZero(value, nameof(value));
        maxInstancesToLaunch = value;
    }

    public static bool Reached()
    {
        var instanceCount = Interlocked.Increment(ref launchedInstances);
        return instanceCount > maxInstancesToLaunch;
    }
}