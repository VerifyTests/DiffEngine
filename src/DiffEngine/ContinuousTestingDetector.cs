namespace DiffEngine;

public static class ContinuousTestingDetector
{
    static ContinuousTestingDetector()
    {
        if (AppDomain.CurrentDomain.GetAssemblies()
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            .Any(a => a.FullName != null &&
                      a.FullName.StartsWith("Microsoft.CodeAnalysis.LiveUnitTesting.Runtime")))
        {
            Detected = true;
            return;
        }

        if (IsNCrunch && !IsNCrunchExplicitRun)
        {
            Detected = true;
        }
    }

    public static bool IsNCrunchExplicitRun { get; } = Environment.GetEnvironmentVariable("NCrunch.IsHighPriority") == "1";
    public static bool Detected { get; set; }
    public static bool IsNCrunch { get; } = Environment.GetEnvironmentVariable("NCRUNCH") != null;
    public static string? NCrunchOriginalProject { get; } = Environment.GetEnvironmentVariable("NCrunch.OriginalProjectPath");
}