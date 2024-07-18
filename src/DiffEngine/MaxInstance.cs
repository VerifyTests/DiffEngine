static class MaxInstance
{
    public static int MaxInstancesToLaunch => capturedMaxInstancesToLaunch ??= GetMaxInstances();

    static int? capturedMaxInstancesToLaunch;
    static int? appDomainMaxInstancesToLaunch;
    static int launchedInstances;
    const int defaultMax = 5;

    static int GetMaxInstances() => GetEnvironmentValue() ??
                                    appDomainMaxInstancesToLaunch ??
                                    defaultMax;

    static int? GetEnvironmentValue()
    {
        var variable = Environment.GetEnvironmentVariable("DiffEngine_MaxInstances");

        if (string.IsNullOrEmpty(variable))
        {
            return null;
        }

        if (ushort.TryParse(variable, out var result))
        {
            return result;
        }

        throw new($"Could not parse the DiffEngine_MaxInstances environment variable: {variable}");
    }

    static void ResetCapturedValue() => capturedMaxInstancesToLaunch = null;

    public static void SetForAppDomain(int value)
    {
        Guard.AgainstNegativeAndZero(value, nameof(value));
        appDomainMaxInstancesToLaunch = value;
        ResetCapturedValue();
    }

    public static void SetForUser(int value)
    {
        Guard.AgainstNegativeAndZero(value, nameof(value));
        EnvironmentHelper.Set("DiffEngine_MaxInstances", value.ToString());
        ResetCapturedValue();
    }

    public static bool Reached()
    {
        var instanceCount = Interlocked.Increment(ref launchedInstances);
        return instanceCount > MaxInstancesToLaunch;
    }
}