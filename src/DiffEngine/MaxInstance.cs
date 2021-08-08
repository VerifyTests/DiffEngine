using System;
using System.Threading;

static class MaxInstance
{
    public static int MaxInstancesToLaunch { get; private set; } = GetMaxInstances();
    static int launchedInstances;
    const int defaultMax = 5;

    static int GetMaxInstances()
    {
        var variable = Environment.GetEnvironmentVariable("DiffEngine_MaxInstances");
        if (string.IsNullOrEmpty(variable))
        {
            return defaultMax;
        }

        if (ushort.TryParse(variable, out var result))
        {
            return result;
        }

        throw new($"Could not parse the DiffEngine_MaxInstances environment variable: {variable}");
    }

    public static void SetForAppDomain(int value)
    {
        Guard.AgainstNegativeAndZero(value, nameof(value));
        MaxInstancesToLaunch = value;
    }

    public static void SetForUser(int value)
    {
        if (MaxInstancesToLaunch == value)
        {
            return;
        }
        MaxInstancesToLaunch = value;
        string? envVariable;
        if (value == defaultMax)
        {
            envVariable = null;
        }
        else
        {
            envVariable = value.ToString();
        }

        Environment.SetEnvironmentVariable("DiffEngine_MaxInstances", envVariable, EnvironmentVariableTarget.User);
    }

    public static bool Reached()
    {
        var instanceCount = Interlocked.Increment(ref launchedInstances);
        return instanceCount > MaxInstancesToLaunch;
    }
}