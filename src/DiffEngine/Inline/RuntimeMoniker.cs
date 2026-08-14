/// <summary>
/// The short target-framework moniker of the running process ("net9.0", "net48"), used to label
/// the origin of an inline patch so a multi-targeted test run can be told apart from a re-run.
/// </summary>
static class RuntimeMoniker
{
    /// <summary>
    /// Null when the framework cannot be determined, which downstream treats as an unlabeled
    /// origin rather than guessing.
    /// </summary>
    public static string? Current { get; } = Map(FrameworkName());

    static string? FrameworkName() =>
#if NET462
        // AppContext.TargetFrameworkName arrived in 4.7.1; this is what it reads there anyway.
        AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName ??
#else
        AppContext.TargetFrameworkName ??
#endif
        Assembly.GetEntryAssembly()?.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;

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
