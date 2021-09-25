using System.Linq;

namespace DiffEngine
{
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

            if (Environment.GetEnvironmentVariable("NCRUNCH") != null &&
                Environment.GetEnvironmentVariable("NCrunch.IsHighPriority") != "1")
            {
                Detected = true;
            }
        }

        public static bool Detected { get; set; }
    }
}