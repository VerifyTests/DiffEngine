static class MaxInstance
{
    public static int MaxInstancesToLaunch => capturedMaxInstancesToLaunch ??= GetMaxInstances();

    static int? capturedMaxInstancesToLaunch;
    static int? appDomainMaxInstancesToLaunch;
    static int launchedInstances;
    const int defaultMax = 5;

    static int GetMaxInstances() =>
        GetEnvironmentValue() ??
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
        Guard.AgainstNegative(value, nameof(value));
        appDomainMaxInstancesToLaunch = value;
        ResetCapturedValue();
    }

    public static void SetForUser(int value)
    {
        Guard.AgainstNegative(value, nameof(value));
        EnvironmentHelper.Set("DiffEngine_MaxInstances", value.ToString());
        ResetCapturedValue();
    }

    /// <summary>
    /// Forgets the instances launched so far. For tests, which need the limit to mean something
    /// definite rather than however many launches the rest of the run happened to make.
    /// </summary>
    internal static void ResetCount() =>
        Interlocked.Exchange(ref launchedInstances, 0);

    public static bool Reached()
    {
        var count = Interlocked.Increment(ref launchedInstances);
        return count > MaxInstancesToLaunch;
    }
}