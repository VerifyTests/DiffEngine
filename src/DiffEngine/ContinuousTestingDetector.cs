namespace DiffEngine;

public static class ContinuousTestingDetector
{
    static ContinuousTestingDetector()
    {
        IsNCrunch = Environment.GetEnvironmentVariable("NCRUNCH") != null;
        if (IsNCrunch)
        {
            IsNCrunchExplicitRun = Environment.GetEnvironmentVariable("NCrunch.IsHighPriority") == "1";
            NCrunchOriginalProjectDirectory = Path.GetDirectoryName(Environment.GetEnvironmentVariable("NCrunch.OriginalProjectPath"));
        }

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

    public static bool IsNCrunchExplicitRun { get; }
    public static bool Detected { get; set; }
    public static bool IsNCrunch { get; }
    public static string? NCrunchOriginalProjectDirectory { get; }
}