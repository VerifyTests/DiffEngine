static class MaxInstance
{
    public static int MaxInstancesToLaunch => capturedMaxInstancesToLaunch ??= GetMaxInstances();

    static int? capturedMaxInstancesToLaunch;
    static int? appDomainMaxInstancesToLaunch;
    static int launchedInstances;
    const int defaultMax = 5;

    /// <summary>
    /// An explicit set wins over the environment, which is only the ambient default for a run that
    /// sets nothing.
    /// <para>
    /// DiffEngine_MaxInstances persists per user - DiffEngineTray writes it to the user
    /// environment on every options save - so reading it first meant that on a machine which had
    /// ever saved Options, <see cref="DiffRunner.MaxInstancesToLaunch" /> silently did nothing. A
    /// test suppressing diff windows with it still opened them. Same order as
    /// <see cref="DiffRunner.Disabled" />, where an explicit set also pins the value.
    /// </para>
    /// </summary>
    static int GetMaxInstances() =>
        appDomainMaxInstancesToLaunch ??
        GetEnvironmentValue() ??
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

    /// <summary>
    /// Persists a user scope value, as <see cref="TargetPosition.SetTargetOnLeft" /> does for
    /// DiffEngine_TargetOnLeft.
    /// <para>
    /// A save that changes nothing writes nothing. The tray calls this for every settings save -
    /// including saves of unrelated settings, like the "always kill locking processes" prompt -
    /// and the value it passes is whatever the options form was populated with. So a machine that
    /// had never chosen a limit still ended up with DiffEngine_MaxInstances in its user
    /// environment, at the value that was already in effect.
    /// </para>
    /// <para>
    /// And choosing the default clears the variable rather than persisting it, so there is a way
    /// back to an unset machine through the options form.
    /// </para>
    /// </summary>
    public static void SetForUser(int value)
    {
        Guard.AgainstNegative(value, nameof(value));

        // The default needs nothing persisted to mean what it means, so returning to it takes the
        // variable back out rather than pinning it at what the default happens to be today.
        string? desired;
        if (value == defaultMax)
        {
            desired = null;
        }
        else
        {
            desired = value.ToString();
        }

        // Only when what is persisted would actually change. Read raw rather than through
        // GetEnvironmentValue, because this is a comparison against the stored text, and because
        // a setter is no place to throw over an existing unparseable value it is about to
        // overwrite anyway.
        if (Environment.GetEnvironmentVariable("DiffEngine_MaxInstances") == desired)
        {
            return;
        }

        EnvironmentHelper.Set("DiffEngine_MaxInstances", desired);
        // The later explicit set is the one that counts, so an app domain value from earlier in
        // the process cannot shadow what the user just chose.
        appDomainMaxInstancesToLaunch = null;
        ResetCapturedValue();
    }

    /// <summary>
    /// Forgets an explicit <see cref="SetForAppDomain" />, so the value is read from the
    /// environment again. For tests, which is where anything sets it and then wants the ambient
    /// value back.
    /// </summary>
    internal static void ResetAppDomainValue()
    {
        appDomainMaxInstancesToLaunch = null;
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
