/// <summary>
/// The short target-framework moniker of the running process ("net9.0", "net48"), used to label
/// the origin of an inline patch so a multi-targeted test run can be told apart from a re-run.
/// </summary>
static class RuntimeMoniker
{
    public const string Key = "DiffEngine.TargetFramework";

    /// <summary>
    /// The consuming project's own $(TargetFramework) when buildTransitive/DiffEngine.targets
    /// stamped it into the runtimeconfig, otherwise the version of the runtime actually
    /// executing. Never AppContext.TargetFrameworkName: that is the entry assembly's
    /// TargetFrameworkAttribute, and in a hosted test run the entry assembly is the runner -
    /// testhost is net8.0, ReSharperTestRunner is netcoreapp3.0 - so every leg of a
    /// multi-targeted run stamped the runner's one moniker, and the queue could neither keep the
    /// legs' variants apart nor settle one without the other. The runtime fallback covers
    /// consumers without the targets, such as linked-source builds: each leg launches on its own
    /// runtime, so it still tells apart what the label exists to tell apart, at the cost of
    /// naming the rolled-forward runtime rather than the target.
    /// <para>
    /// .NET Framework keeps the attribute path, and with it null when nothing can be determined:
    /// it has no runtimeconfig to carry a stamp, its Environment.Version is the CLR's own
    /// (4.0.30319) whatever the target, and every net4x moniker runs on that one CLR.
    /// </para>
    /// </summary>
    public static string? Current { get; } =
#if NETFRAMEWORK
        Map(FrameworkName());
#else
        AppContext.GetData(Key) is string { Length: > 0 } configured
            ? configured
            : $"net{Environment.Version.Major}.{Environment.Version.Minor}";
#endif

#if NETFRAMEWORK
    static string? FrameworkName() =>
#if NET462
        // AppContext.TargetFrameworkName arrived in 4.7.1; this is what it reads there anyway.
        AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName ??
#else
        AppContext.TargetFrameworkName ??
#endif
        Assembly.GetEntryAssembly()?.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
#endif

    // Internal seam for tests; the input shape is "{identifier},Version=v{version}[,Profile=...]".
    internal static string? Map(string? frameworkName)
    {
        if (string.IsNullOrWhiteSpace(frameworkName))
        {
            return null;
        }

        // ReSharper disable once RedundantSuppressNullableWarningExpression
        var parts = frameworkName!.Split(',');
        var identifier = parts[0].Trim();
        string? versionText = null;
        for (var index = 1; index < parts.Length; index++)
        {
            var part = parts[index].Trim();
            if (part.StartsWith("Version=v", StringComparison.Ordinal))
            {
                versionText = part["Version=v".Length..];
                break;
            }
        }

        if (string.IsNullOrEmpty(versionText) ||
            !Version.TryParse(versionText, out var version))
        {
            return null;
        }

        var minor = Math.Max(version.Minor, 0);
        return identifier switch
        {
            ".NETCoreApp" when version.Major >= 5 => $"net{version.Major}.{minor}",
            ".NETCoreApp" => $"netcoreapp{version.Major}.{minor}",
            // "4.6.2" reads as net462, matching how TFMs are written.
            // ReSharper disable once RedundantSuppressNullableWarningExpression
            ".NETFramework" => "net" + versionText!.Replace(".", ""),
            ".NETStandard" => $"netstandard{version.Major}.{minor}",
            _ => null
        };
    }
}
